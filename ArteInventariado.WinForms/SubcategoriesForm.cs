using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Services;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

public sealed class SubcategoriesForm : Form
{
    private readonly ISubcategoryService _subcategoryService;
    private readonly ICategoryService _categoryService;
    private readonly IInstallationContext _installationContext;
    private readonly BindingSource _bindingSource = new();

    private readonly Button _btnNueva = new();
    private readonly Button _btnEditar = new();
    private readonly Button _btnActivar = new();
    private readonly Button _btnDesactivar = new();
    private readonly Button _btnEliminar = new();
    private readonly Button _btnActualizar = new();
    private readonly ComboBox _cmbCategoria = new();
    private readonly ComboBox _cmbEstado = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblModo = new();
    private readonly Label _lblStatus = new();

    private List<CategorySummary> _categories = [];
    private List<SubcategorySummary> _subcategories = [];
    private bool _loading;

    public SubcategoriesForm(
        ISubcategoryService subcategoryService,
        ICategoryService categoryService,
        IInstallationContext installationContext,
        bool loadDataOnShow = true)
    {
        _subcategoryService = subcategoryService;
        _categoryService = categoryService;
        _installationContext = installationContext;

        BuildUi();
        if (loadDataOnShow)
        {
            Load += async (_, _) => await RefreshSubcategoriesAsync();
        }
    }

    internal Button NewButtonForSmoke => _btnNueva;
    internal Button EditButtonForSmoke => _btnEditar;
    internal Button ActivateButtonForSmoke => _btnActivar;
    internal Button DeactivateButtonForSmoke => _btnDesactivar;
    internal Button RefreshButtonForSmoke => _btnActualizar;
    internal ComboBox CategoryFilterForSmoke => _cmbCategoria;
    internal ComboBox StatusFilterForSmoke => _cmbEstado;
    internal DataGridView GridForSmoke => _grid;
    internal Label StatusLabelForSmoke => _lblStatus;
    internal Label ModeLabelForSmoke => _lblModo;
    internal IReadOnlyList<SubcategorySummary> LoadedSubcategoriesForSmoke => _subcategories;

    internal Task RefreshForSmokeAsync() => RefreshSubcategoriesAsync();

    private void BuildUi()
    {
        Text = "Administración de subcategorías";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1160, 700);
        MinimumSize = new Size(780, 520);
        UITheme.StyleForm(this);

        ConfigureButton(_btnNueva, "Nueva subcategoría", BtnNueva_Click, UITheme.StylePrimaryButton, 175);
        ConfigureButton(_btnEditar, "Editar subcategoría", BtnEditar_Click, UITheme.StyleSecondaryButton, 180);
        ConfigureButton(_btnActivar, "Activar", BtnActivar_Click, UITheme.StyleOutlineButton, 105);
        ConfigureButton(_btnDesactivar, "Desactivar", BtnDesactivar_Click, UITheme.StyleDangerButton, 120);
        ConfigureButton(_btnEliminar, "Eliminar", BtnEliminar_Click, UITheme.StyleDangerButton, 105);
        ConfigureButton(_btnActualizar, "Actualizar", async (_, _) => await RefreshSubcategoriesAsync(), UITheme.StyleOutlineButton, 120);

        ConfigureFilter(_cmbCategoria, "Categoría");
        ConfigureFilter(_cmbEstado, "Estado");
        _cmbEstado.DisplayMember = nameof(StatusFilterOption.Nombre);
        _cmbEstado.ValueMember = nameof(StatusFilterOption.Value);
        _cmbEstado.DataSource = new List<StatusFilterOption>
        {
            new(StatusFilter.All, "Todos los estados"),
            new(StatusFilter.Assignable, "Activas asignables"),
            new(StatusFilter.ActiveNotAssignable, "Activas no asignables"),
            new(StatusFilter.Inactive, "Inactivas")
        };
        _cmbCategoria.SelectedIndexChanged += (_, _) => ApplyFilters();
        _cmbEstado.SelectedIndexChanged += (_, _) => ApplyFilters();

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.MultiSelect = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.DataSource = _bindingSource;
        _grid.CellDoubleClick += (_, _) => BtnEditar_Click(_grid, EventArgs.Empty);
        UITheme.StyleDataGridView(_grid);
        AddGridColumn(nameof(SubcategoryGridRow.Nombre), "Subcategoría", 190);
        AddGridColumn(nameof(SubcategoryGridRow.Categoria), "Categoría", 190);
        AddGridColumn(nameof(SubcategoryGridRow.Descripcion), "Descripción", 280, fill: true);
        AddGridColumn(nameof(SubcategoryGridRow.Estado), "Estado", 220, centered: true);

        UITheme.StyleStatusLabel(_lblModo, UITheme.SecondaryColor);
        _lblModo.Dock = DockStyle.Fill;
        UITheme.StyleStatusLabel(_lblStatus);
        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.Text = "Preparando subcategorías...";

        var filters = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(UITheme.SpaceMd, UITheme.SpaceXs, UITheme.SpaceMd, UITheme.SpaceXs),
            BackColor = UITheme.SurfaceColor,
            Margin = Padding.Empty
        };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        filters.Controls.Add(VisualLayout.CreateField("Filtrar por categoría", _cmbCategoria), 0, 0);
        filters.Controls.Add(VisualLayout.CreateField("Filtrar por estado", _cmbEstado), 1, 0);

        var actions = VisualLayout.CreateActionBar(
            _btnNueva, _btnEditar, _btnActivar, _btnDesactivar, _btnEliminar, _btnActualizar);
        var gridHost = VisualLayout.CreateSection(
            "Subcategorías registradas",
            "Las subcategorías bajo una categoría inactiva se conservan visibles, pero no son asignables.",
            _grid);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(UITheme.SpaceLg),
            BackColor = UITheme.BackgroundColor,
            Margin = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.Controls.Add(VisualLayout.CreatePageHeader(
            "Subcategorías",
            "Organiza los productos dentro de cada categoría."), 0, 0);
        root.Controls.Add(_lblModo, 0, 1);
        root.Controls.Add(filters, 0, 2);
        root.Controls.Add(actions, 0, 3);
        root.Controls.Add(gridHost, 0, 4);
        root.Controls.Add(_lblStatus, 0, 5);
        Controls.Add(root);

        UpdateModePresentation();
        ApplyActionState(enabled: true);
    }

    private async Task RefreshSubcategoriesAsync()
    {
        if (_loading || IsDisposed)
        {
            return;
        }

        _loading = true;
        ApplyActionState(enabled: false);
        try
        {
            var categories = await _categoryService.GetAllAsync() ?? [];
            var subcategories = await _subcategoryService.GetAllAsync() ?? [];
            _categories = categories.ToList();
            _subcategories = subcategories.ToList();
            LoadCategoryFilterOptions();
            ApplyFilters();
        }
        catch (Exception ex)
        {
            _categories = [];
            _subcategories = [];
            _bindingSource.DataSource = Array.Empty<SubcategoryGridRow>();
            _lblStatus.Text = "No se pudieron cargar las subcategorías.";
            MessageBox.Show(ex.Message, "Subcategorías", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _loading = false;
            ApplyActionState(enabled: true);
        }
    }

    private void LoadCategoryFilterOptions()
    {
        var options = new List<CategoryFilterOption>
        {
            new(0, "Todas las categorías")
        };
        options.AddRange(_categories
            .OrderBy(category => category.Nombre)
            .Select(category => new CategoryFilterOption(category.CategoriaId, category.Nombre)));

        _cmbCategoria.SelectedIndexChanged -= CategoryFilterChanged;
        _cmbCategoria.DataSource = null;
        _cmbCategoria.DisplayMember = nameof(CategoryFilterOption.Nombre);
        _cmbCategoria.ValueMember = nameof(CategoryFilterOption.CategoriaId);
        _cmbCategoria.DataSource = options;
        _cmbCategoria.SelectedIndex = 0;
        _cmbCategoria.SelectedIndexChanged += CategoryFilterChanged;
    }

    private void CategoryFilterChanged(object? sender, EventArgs e) => ApplyFilters();

    private void ApplyFilters()
    {
        var categoryId = (_cmbCategoria.SelectedItem as CategoryFilterOption)?.CategoriaId ?? 0;
        var status = (_cmbEstado.SelectedItem as StatusFilterOption)?.Value ?? StatusFilter.All;
        var rows = _subcategories
            .Where(subcategory => categoryId == 0 || subcategory.CategoriaId == categoryId)
            .Select(ToGridRow)
            .Where(row => MatchesStatus(row, status))
            .OrderBy(row => row.Categoria)
            .ThenBy(row => row.Nombre)
            .ToList();

        _bindingSource.DataSource = rows;
        if (_subcategories.Count == 0)
        {
            _lblStatus.Text = "No hay subcategorías registradas.";
        }
        else if (rows.Count == 0)
        {
            _lblStatus.Text = "No hay subcategorías que coincidan con los filtros.";
        }
        else
        {
            _lblStatus.Text = $"{rows.Count} subcategoría(s) visibles.";
        }
    }

    private SubcategoryGridRow ToGridRow(SubcategorySummary subcategory)
    {
        var category = _categories.FirstOrDefault(item => item.CategoriaId == subcategory.CategoriaId);
        var protectedParent = category is not null && IsProtectedCategory(category.Nombre);
        var parentActive = category?.Activa == true;
        var assignable = subcategory.Activa && parentActive && !protectedParent;
        var state = !subcategory.Activa
            ? parentActive ? "Inactiva" : "Inactiva · padre inactiva"
            : assignable ? "Activa"
            : "Activa · no asignable";

        return new SubcategoryGridRow(
            subcategory.SubcategoriaId,
            subcategory.Nombre,
            category?.Nombre ?? "No disponible",
            subcategory.Descripcion ?? "No disponible",
            state,
            subcategory.Activa,
            assignable);
    }

    private static bool MatchesStatus(SubcategoryGridRow row, StatusFilter status)
    {
        return status switch
        {
            StatusFilter.Assignable => row.Assignable,
            StatusFilter.ActiveNotAssignable => row.Activa && !row.Assignable,
            StatusFilter.Inactive => !row.Activa,
            _ => true
        };
    }

    private async void BtnNueva_Click(object? sender, EventArgs e)
    {
        if (!EnsureWritesAllowed("Nueva subcategoría"))
        {
            return;
        }

        using var form = new SubcategoryEditForm(
            _subcategoryService,
            _categoryService,
            _installationContext);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            MessageBox.Show("Subcategoría creada.", "Subcategorías", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshSubcategoriesAsync();
        }
    }

    private async void BtnEditar_Click(object? sender, EventArgs e)
    {
        if (!EnsureWritesAllowed("Editar subcategoría"))
        {
            return;
        }

        var subcategory = GetSelectedSubcategory();
        if (subcategory is null)
        {
            return;
        }

        using var form = new SubcategoryEditForm(
            _subcategoryService,
            _categoryService,
            _installationContext,
            subcategory);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            MessageBox.Show("Subcategoría editada.", "Subcategorías", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshSubcategoriesAsync();
        }
    }

    private async void BtnActivar_Click(object? sender, EventArgs e)
    {
        await SetSelectedSubcategoryActiveAsync(true, "Subcategoría activada.");
    }

    private async void BtnDesactivar_Click(object? sender, EventArgs e)
    {
        await SetSelectedSubcategoryActiveAsync(false, "Subcategoría desactivada.");
    }

    private async void BtnEliminar_Click(object? sender, EventArgs e)
    {
        if (!EnsureWritesAllowed("Eliminar subcategoría"))
        {
            return;
        }

        var subcategory = GetSelectedSubcategory();
        if (subcategory is null)
        {
            return;
        }

        if (MessageBox.Show(
                $"¿Eliminar la subcategoría '{subcategory.Nombre}'? Solo se podrá si no está asignada a productos ni aparece en el historial. Si está en uso, puedes desactivarla.",
                "Eliminar subcategoría",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            await _subcategoryService.DeleteAsync(subcategory.SubcategoriaId);
            MessageBox.Show("Subcategoría eliminada.", "Subcategorías", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshSubcategoriesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "No se pudo eliminar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task SetSelectedSubcategoryActiveAsync(bool active, string successMessage)
    {
        if (!EnsureWritesAllowed(active ? "Activar subcategoría" : "Desactivar subcategoría"))
        {
            return;
        }

        var subcategory = GetSelectedSubcategory();
        if (subcategory is null)
        {
            return;
        }

        try
        {
            await _subcategoryService.SetActiveAsync(subcategory.SubcategoriaId, active);
            MessageBox.Show(successMessage, "Subcategorías", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshSubcategoriesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Subcategorías", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private SubcategorySummary? GetSelectedSubcategory()
    {
        if (_grid.CurrentRow?.DataBoundItem is not SubcategoryGridRow row)
        {
            MessageBox.Show(
                "Selecciona una subcategoría.",
                "Subcategorías",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return null;
        }

        return _subcategories.FirstOrDefault(item => item.SubcategoriaId == row.SubcategoriaId);
    }

    private bool EnsureWritesAllowed(string operation)
    {
        try
        {
            _installationContext.EnsureOperationalWriteAllowed(operation);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Subcategorías", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private void ApplyActionState(bool enabled)
    {
        var writesAllowed = enabled && _installationContext.AllowsOperationalWrites;
        _btnNueva.Enabled = writesAllowed;
        _btnEditar.Enabled = writesAllowed;
        _btnActivar.Enabled = writesAllowed;
        _btnDesactivar.Enabled = writesAllowed;
        _btnEliminar.Enabled = writesAllowed;
        _btnActualizar.Enabled = enabled;
        UpdateModePresentation();
    }

    private void UpdateModePresentation()
    {
        _lblModo.Text = _installationContext.Status.Mode == InstallationMode.Consulta
            ? "Modo Consulta: puedes revisar y filtrar, pero las acciones de escritura están deshabilitadas."
            : "Modo Principal: puedes administrar las subcategorías según tu sesión ADMIN.";
        _lblModo.ForeColor = _installationContext.Status.Mode == InstallationMode.Consulta
            ? Color.FromArgb(120, 67, 10)
            : UITheme.SecondaryColor;
        _lblModo.BackColor = _installationContext.Status.Mode == InstallationMode.Consulta
            ? Color.FromArgb(255, 235, 156)
            : UITheme.SurfaceMutedColor;
    }

    private void ConfigureFilter(ComboBox combo, string accessibleName)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.AccessibleName = accessibleName;
    }

    private void AddGridColumn(
        string propertyName,
        string headerText,
        int width,
        bool fill = false,
        bool centered = false)
    {
        var column = new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = headerText,
            Width = width,
            MinimumWidth = Math.Min(width, 90),
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None
        };
        if (centered)
        {
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
        _grid.Columns.Add(column);
    }

    private static void ConfigureButton(
        Button button,
        string text,
        EventHandler clickHandler,
        Action<Button> style,
        int minimumWidth)
    {
        button.Text = text;
        button.Click += clickHandler;
        style(button);
        UITheme.EnsureButtonContentFits(button, minimumWidth, UITheme.StandardControlHeight, 32);
        button.Size = button.MinimumSize;
    }

    private static bool IsProtectedCategory(string? categoryName)
    {
        return string.Equals(
            categoryName?.Trim(),
            CategoryService.DefaultCategoryName,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CategoryFilterOption(int CategoriaId, string Nombre);

    private sealed record StatusFilterOption(StatusFilter Value, string Nombre)
    {
        public override string ToString() => Nombre;
    }

    private enum StatusFilter
    {
        All,
        Assignable,
        ActiveNotAssignable,
        Inactive
    }

    private sealed record SubcategoryGridRow(
        int SubcategoriaId,
        string Nombre,
        string Categoria,
        string Descripcion,
        string Estado,
        bool Activa,
        bool Assignable);
}
