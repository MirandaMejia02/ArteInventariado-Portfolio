using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

public class CategoriesForm : Form
{
    private readonly ICategoryService _categoryService;
    private readonly BindingSource _bindingSource = new();

    private readonly Button _btnNueva = new();
    private readonly Button _btnEditar = new();
    private readonly Button _btnActivar = new();
    private readonly Button _btnDesactivar = new();
    private readonly Button _btnEliminar = new();
    private readonly Button _btnRefrescar = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblStatus = new();

    private List<CategorySummary> _categories = [];

    public CategoriesForm(ICategoryService categoryService)
    {
        _categoryService = categoryService;

        BuildUi();
        Load += CategoriesForm_Load;
    }

    private async void CategoriesForm_Load(object? sender, EventArgs e)
    {
        await RefreshCategoriesAsync();
    }

    private void BuildUi()
    {
        Text = "Administracion de categorias";
        StartPosition = FormStartPosition.CenterParent;
        // Keep the action bar on one line and give the category list more room.
        ClientSize = new Size(1160, 700);
        MinimumSize = new Size(760, 520);
        UITheme.StyleForm(this);

        ConfigureButton(_btnNueva, "Nueva categoría", BtnNueva_Click, UITheme.StylePrimaryButton, 160);
        ConfigureButton(_btnEditar, "Editar categoría", BtnEditar_Click, UITheme.StyleSecondaryButton, 165);
        ConfigureButton(_btnActivar, "Activar", BtnActivar_Click, UITheme.StyleOutlineButton, 105);
        ConfigureButton(_btnDesactivar, "Desactivar", BtnDesactivar_Click, UITheme.StyleDangerButton, 120);
        ConfigureButton(_btnEliminar, "Eliminar", BtnEliminar_Click, UITheme.StyleDangerButton, 105);
        ConfigureButton(_btnRefrescar, "Actualizar", async (_, _) => await RefreshCategoriesAsync(), UITheme.StyleOutlineButton, 120);

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

        AddGridColumn("Nombre", "Categoría", 180, fill: true);
        AddGridColumn("Descripcion", "Descripción", 310, fill: true);
        AddGridColumn("Activa", "Estado", 90, centered: true);
        AddGridColumn("Protegida", "Protegida", 100, centered: true);

        var actions = VisualLayout.CreateActionBar(
            _btnNueva, _btnEditar, _btnActivar, _btnDesactivar, _btnEliminar, _btnRefrescar);
        var gridHost = VisualLayout.CreateSection(
            "Listado de categorías",
            "La categoría protegida “Sin categoría” conserva la compatibilidad histórica.",
            _grid);
        UITheme.StyleStatusLabel(_lblStatus);
        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.Text = "Preparando categorías...";

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(UITheme.SpaceLg),
            BackColor = UITheme.BackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.Controls.Add(VisualLayout.CreatePageHeader(
            "Categorías",
            "Organiza el catálogo sin alterar productos ni históricos."), 0, 0);
        root.Controls.Add(actions, 0, 1);
        root.Controls.Add(gridHost, 0, 2);
        root.Controls.Add(_lblStatus, 0, 3);
        Controls.Add(root);
    }

    private async Task RefreshCategoriesAsync()
    {
        try
        {
            SetActionsEnabled(false);
            _categories = (await _categoryService.GetAllAsync()).ToList();
            _bindingSource.DataSource = _categories.Select(CategoryGridRow.FromSummary).ToList();
            _lblStatus.Text = _categories.Count == 0
                ? "No hay categorías registradas."
                : $"{_categories.Count} categoría(s) registradas.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Categorias", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetActionsEnabled(true);
        }
    }

    private async void BtnNueva_Click(object? sender, EventArgs e)
    {
        using var form = new CategoryEditForm(_categoryService);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            MessageBox.Show("Categoria creada.", "Categorias", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshCategoriesAsync();
        }
    }

    private async void BtnEditar_Click(object? sender, EventArgs e)
    {
        var category = GetSelectedCategory();
        if (category is null)
        {
            return;
        }

        if (IsDefaultCategory(category.Nombre))
        {
            MessageBox.Show("La categoria 'Sin categoria' no se puede editar.", "Categorias", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new CategoryEditForm(_categoryService, category);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            MessageBox.Show("Categoria editada.", "Categorias", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshCategoriesAsync();
        }
    }

    private async void BtnActivar_Click(object? sender, EventArgs e)
    {
        await SetSelectedCategoryActiveAsync(true, "Categoria activada.");
    }

    private async void BtnDesactivar_Click(object? sender, EventArgs e)
    {
        await SetSelectedCategoryActiveAsync(false, "Categoria desactivada.");
    }

    private async void BtnEliminar_Click(object? sender, EventArgs e)
    {
        var category = GetSelectedCategory();
        if (category is null)
        {
            return;
        }

        if (IsDefaultCategory(category.Nombre))
        {
            MessageBox.Show(
                "La categoría 'Sin categoría' está protegida.",
                "Categorías",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(
                $"¿Eliminar la categoría '{category.Nombre}'? Solo se eliminará si no tiene productos, subcategorías ni historial asociado. Si está en uso, puedes desactivarla.",
                "Eliminar categoría",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            await _categoryService.DeleteAsync(category.CategoriaId);
            MessageBox.Show("Categoría eliminada.", "Categorías", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshCategoriesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "No se pudo eliminar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task SetSelectedCategoryActiveAsync(bool active, string successMessage)
    {
        var category = GetSelectedCategory();
        if (category is null)
        {
            return;
        }

        if (IsDefaultCategory(category.Nombre))
        {
            MessageBox.Show("La categoria 'Sin categoria' no se puede activar ni desactivar desde esta pantalla.", "Categorias", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            await _categoryService.SetActiveAsync(category.CategoriaId, active);
            MessageBox.Show(successMessage, "Categorias", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshCategoriesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Categorias", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private CategorySummary? GetSelectedCategory()
    {
        if (_grid.CurrentRow?.DataBoundItem is not CategoryGridRow row)
        {
            MessageBox.Show("Seleccione una categoria.", "Categorias", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        return _categories.FirstOrDefault(c => c.CategoriaId == row.CategoriaId);
    }

    private void SetActionsEnabled(bool enabled)
    {
        _btnNueva.Enabled = enabled;
        _btnEditar.Enabled = enabled;
        _btnActivar.Enabled = enabled;
        _btnDesactivar.Enabled = enabled;
        _btnEliminar.Enabled = enabled;
        _btnRefrescar.Enabled = enabled;
    }

    private void AddGridColumn(string propertyName, string headerText, int width, bool fill = false, bool centered = false)
    {
        var column = new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = headerText,
            Width = width,
            MinimumWidth = width,
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

    private static bool IsDefaultCategory(string categoryName)
    {
        return string.Equals(categoryName, "Sin categoria", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CategoryGridRow(
        int CategoriaId,
        string Nombre,
        string? Descripcion,
        string Activa,
        string Protegida)
    {
        public static CategoryGridRow FromSummary(CategorySummary category)
        {
            return new CategoryGridRow(
                category.CategoriaId,
                category.Nombre,
                category.Descripcion,
                category.Activa ? "Si" : "No",
                CategoriesForm.IsDefaultCategory(category.Nombre) ? "Si" : "No");
        }
    }
}
