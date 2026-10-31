using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Infrastructure.Services;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

public sealed class SubcategoryEditForm : Form
{
    private readonly ISubcategoryService _subcategoryService;
    private readonly ICategoryService _categoryService;
    private readonly IInstallationContext _installationContext;
    private readonly SubcategorySummary? _subcategory;
    private readonly bool _loadDataOnShow;

    private readonly ComboBox _cmbCategoria = new();
    private readonly TextBox _txtNombre = new();
    private readonly TextBox _txtDescripcion = new();
    private readonly Label _lblInfo = new();
    private readonly Button _btnGuardar = new();
    private readonly Button _btnCancelar = new();

    private bool _loading;
    private bool _saving;

    public SubcategoryEditForm(
        ISubcategoryService subcategoryService,
        ICategoryService categoryService,
        IInstallationContext installationContext,
        SubcategorySummary? subcategory = null,
        bool loadDataOnShow = true)
    {
        _subcategoryService = subcategoryService;
        _categoryService = categoryService;
        _installationContext = installationContext;
        _subcategory = subcategory;
        _loadDataOnShow = loadDataOnShow;

        BuildUi();
        if (_loadDataOnShow)
        {
            Load += async (_, _) => await LoadCategoriesAsync();
        }
    }

    private bool IsEdit => _subcategory is not null;

    internal ComboBox CategorySelectorForSmoke => _cmbCategoria;
    internal TextBox NameInputForSmoke => _txtNombre;
    internal Button SaveButtonForSmoke => _btnGuardar;
    internal Label InfoLabelForSmoke => _lblInfo;

    internal Task LoadForSmokeAsync() => LoadCategoriesAsync();

    private void BuildUi()
    {
        Text = IsEdit ? "Editar subcategoría" : "Nueva subcategoría";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(590, 520);
        MinimumSize = new Size(520, 480);
        UITheme.StyleForm(this);

        _cmbCategoria.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbCategoria.AccessibleName = "Categoría";
        _cmbCategoria.AutoSize = false;
        _cmbCategoria.Height = UITheme.CompactControlHeight;

        _txtNombre.AccessibleName = "Nombre";
        _txtNombre.MaxLength = 120;
        _txtNombre.AutoSize = false;
        _txtNombre.Height = UITheme.CompactControlHeight;

        _txtDescripcion.AccessibleName = "Descripción";
        _txtDescripcion.Multiline = true;
        _txtDescripcion.ScrollBars = ScrollBars.Vertical;
        _txtDescripcion.MinimumSize = new Size(0, 110);

        UITheme.StyleStatusLabel(_lblInfo, UITheme.SecondaryColor);
        _lblInfo.Dock = DockStyle.Fill;
        _lblInfo.Text = IsEdit
            ? "La categoría padre está bloqueada en este bloque."
            : "Selecciona una categoría activa para crear la subcategoría.";

        ConfigureButton(_btnGuardar, "Guardar", BtnGuardar_Click, UITheme.StylePrimaryButton, 120);
        ConfigureButton(_btnCancelar, "Cancelar", (_, _) => Close(), UITheme.StyleOutlineButton, 120);

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(UITheme.SpaceLg),
            BackColor = UITheme.SurfaceColor,
            Margin = Padding.Empty
        };
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        fields.Controls.Add(VisualLayout.CreateField("Categoría", _cmbCategoria), 0, 0);
        fields.Controls.Add(VisualLayout.CreateField("Nombre", _txtNombre), 0, 1);
        fields.Controls.Add(CreateMultilineField("Descripción", _txtDescripcion), 0, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(UITheme.SpaceLg, UITheme.SpaceSm, UITheme.SpaceLg, UITheme.SpaceSm),
            Margin = Padding.Empty
        };
        _btnCancelar.Margin = new Padding(UITheme.SpaceSm, 0, 0, 0);
        actions.Controls.Add(_btnCancelar);
        actions.Controls.Add(_btnGuardar);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = UITheme.BackgroundColor,
            Margin = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.Controls.Add(VisualLayout.CreatePageHeader(
            IsEdit ? "Editar subcategoría" : "Nueva subcategoría",
            "Organiza los productos dentro de cada categoría."), 0, 0);
        root.Controls.Add(fields, 0, 1);
        root.Controls.Add(_lblInfo, 0, 2);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);

        AcceptButton = _btnGuardar;
        CancelButton = _btnCancelar;

        if (IsEdit)
        {
            _txtNombre.Text = _subcategory!.Nombre;
            _txtDescripcion.Text = _subcategory.Descripcion ?? string.Empty;
            _cmbCategoria.Enabled = false;
        }
    }

    private async Task LoadCategoriesAsync()
    {
        if (_loading || IsDisposed)
        {
            return;
        }

        _loading = true;
        SetLoadingState();
        try
        {
            var categories = (IsEdit
                    ? await _categoryService.GetAllAsync()
                    : await _categoryService.GetActiveAsync())
                ?.ToList() ?? [];

            var options = IsEdit
                ? BuildEditOptions(categories)
                : categories
                    .Where(category => category.Activa && !IsProtectedCategory(category.Nombre))
                    .OrderBy(category => category.Nombre)
                    .Select(category => new CategoryOption(category.CategoriaId, category.Nombre))
                    .ToList();

            _cmbCategoria.DataSource = null;
            _cmbCategoria.DisplayMember = nameof(CategoryOption.Nombre);
            _cmbCategoria.ValueMember = nameof(CategoryOption.CategoriaId);
            _cmbCategoria.DataSource = options;
            if (options.Count > 0)
            {
                _cmbCategoria.SelectedIndex = 0;
            }

            if (IsEdit)
            {
                _cmbCategoria.Enabled = false;
                _lblInfo.Text = options.Count == 0
                    ? "No se encontró la categoría padre; el registro histórico se conserva sin moverlo."
                    : "La categoría padre está bloqueada en este bloque.";
            }
            else if (options.Count == 0)
            {
                _lblInfo.Text = "No hay categorías activas disponibles para crear una subcategoría.";
            }
            else
            {
                _lblInfo.Text = "Selecciona una categoría activa para crear la subcategoría.";
            }
        }
        catch (Exception ex)
        {
            _cmbCategoria.DataSource = null;
            _lblInfo.Text = "No se pudieron cargar las categorías.";
            MessageBox.Show(ex.Message, "Subcategorías", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _loading = false;
            SetLoadingState();
        }
    }

    private List<CategoryOption> BuildEditOptions(IReadOnlyList<CategorySummary> categories)
    {
        if (_subcategory is null)
        {
            return [];
        }

        var category = categories.FirstOrDefault(item => item.CategoriaId == _subcategory.CategoriaId);
        return category is null
            ? [new CategoryOption(_subcategory.CategoriaId, $"Categoría no disponible (ID {_subcategory.CategoriaId})")]
            : [new CategoryOption(category.CategoriaId, category.Nombre)];
    }

    private async void BtnGuardar_Click(object? sender, EventArgs e)
    {
        if (_saving || _loading)
        {
            return;
        }

        if (!TryValidateInput(out var categoryId))
        {
            return;
        }

        try
        {
            _installationContext.EnsureOperationalWriteAllowed(
                IsEdit ? "Editar subcategoría" : "Nueva subcategoría");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Subcategorías", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _saving = true;
        SetSavingState();
        try
        {
            if (_subcategory is null)
            {
                await _subcategoryService.CreateAsync(new CreateSubcategoryRequest(
                    categoryId!.Value,
                    _txtNombre.Text,
                    _txtDescripcion.Text));
            }
            else
            {
                await _subcategoryService.UpdateAsync(new UpdateSubcategoryRequest(
                    _subcategory.SubcategoriaId,
                    _txtNombre.Text,
                    _txtDescripcion.Text));
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Subcategorías", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _saving = false;
            SetSavingState();
        }
    }

    private bool TryValidateInput(out int? categoryId)
    {
        categoryId = null;
        if (!IsEdit)
        {
            categoryId = GetSelectedCategoryId();
            if (!categoryId.HasValue)
            {
                MessageBox.Show(
                    "Selecciona una categoría activa antes de guardar.",
                    "Subcategorías",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(_txtNombre.Text))
        {
            MessageBox.Show(
                "El nombre de la subcategoría es obligatorio.",
                "Subcategorías",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            _txtNombre.Focus();
            return false;
        }

        return true;
    }

    private int? GetSelectedCategoryId()
    {
        return _cmbCategoria.SelectedItem is CategoryOption option
            ? option.CategoriaId
            : null;
    }

    private void SetLoadingState()
    {
        _cmbCategoria.Enabled = !_loading && !IsEdit;
        _txtNombre.Enabled = !_loading;
        _txtDescripcion.Enabled = !_loading;
        _btnCancelar.Enabled = !_saving;
        _btnGuardar.Enabled = !_loading && !_saving && (IsEdit || _cmbCategoria.Items.Count > 0);
    }

    private void SetSavingState()
    {
        _btnGuardar.Enabled = !_saving && !_loading && (IsEdit || _cmbCategoria.Items.Count > 0);
        _btnCancelar.Enabled = !_saving;
        _cmbCategoria.Enabled = !_saving && !IsEdit;
        _txtNombre.Enabled = !_saving;
        _txtDescripcion.Enabled = !_saving;
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

    private static TableLayoutPanel CreateMultilineField(string labelText, TextBox input)
    {
        var field = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(UITheme.SpaceXs)
        };
        field.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        field.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        field.Controls.Add(new Label
        {
            Text = labelText,
            AutoSize = true,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            Margin = new Padding(0, 0, 0, UITheme.SpaceXs)
        }, 0, 0);
        UITheme.StyleInput(input);
        input.Dock = DockStyle.Fill;
        input.Margin = Padding.Empty;
        field.Controls.Add(input, 0, 1);
        return field;
    }

    private static bool IsProtectedCategory(string? categoryName)
    {
        return string.Equals(
            categoryName?.Trim(),
            CategoryService.DefaultCategoryName,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CategoryOption(int CategoriaId, string Nombre);
}
