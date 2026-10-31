using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

public class CategoryEditForm : Form
{
    private readonly ICategoryService _categoryService;
    private readonly CategorySummary? _category;
    private readonly TextBox _txtNombre = new();
    private readonly TextBox _txtDescripcion = new();
    private readonly Button _btnGuardar = new();
    private readonly Button _btnCancelar = new();

    public CategoryEditForm(ICategoryService categoryService, CategorySummary? category = null)
    {
        _categoryService = categoryService;
        _category = category;

        BuildUi();
        LoadCategoryValues();
    }

    private bool IsEdit => _category is not null;

    private void BuildUi()
    {
        Text = IsEdit ? "Editar categoria" : "Crear categoria";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 390);
        MinimumSize = new Size(480, 360);
        UITheme.StyleForm(this);

        _txtDescripcion.Multiline = true;
        _txtDescripcion.ScrollBars = ScrollBars.Vertical;
        _txtDescripcion.MinimumSize = new Size(0, 105);

        _btnGuardar.Text = "Guardar";
        _btnGuardar.Click += BtnGuardar_Click;
        UITheme.StylePrimaryButton(_btnGuardar);
        UITheme.EnsureButtonContentFits(_btnGuardar, 120, 42, 36);
        _btnGuardar.Size = _btnGuardar.MinimumSize;

        _btnCancelar.Text = "Cancelar";
        _btnCancelar.Click += (_, _) => Close();
        UITheme.StyleOutlineButton(_btnCancelar);
        UITheme.EnsureButtonContentFits(_btnCancelar, 120, 42, 36);
        _btnCancelar.Size = _btnCancelar.MinimumSize;

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UITheme.SpaceLg),
            BackColor = UITheme.SurfaceColor
        };
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        fields.Controls.Add(VisualLayout.CreateField("Nombre", _txtNombre,
            "Usa un nombre breve y reconocible en búsquedas y filtros."), 0, 0);
        fields.Controls.Add(VisualLayout.CreateField("Descripción", _txtDescripcion), 0, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(UITheme.SpaceLg, UITheme.SpaceSm, UITheme.SpaceLg, UITheme.SpaceSm)
        };
        _btnCancelar.Margin = new Padding(UITheme.SpaceSm, 0, 0, 0);
        actions.Controls.Add(_btnCancelar);
        actions.Controls.Add(_btnGuardar);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.Controls.Add(VisualLayout.CreatePageHeader(
            IsEdit ? "Editar categoría" : "Nueva categoría",
            IsEdit ? "Actualiza la información visible de la categoría." : "Crea una categoría para organizar el catálogo."), 0, 0);
        root.Controls.Add(fields, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
        AcceptButton = _btnGuardar;
        CancelButton = _btnCancelar;
    }

    private void LoadCategoryValues()
    {
        if (_category is null)
        {
            return;
        }

        _txtNombre.Text = _category.Nombre;
        _txtDescripcion.Text = _category.Descripcion;

        if (IsDefaultCategory(_category.Nombre))
        {
            _txtNombre.Enabled = false;
            _txtDescripcion.Enabled = false;
            _btnGuardar.Enabled = false;
        }
    }

    private async void BtnGuardar_Click(object? sender, EventArgs e)
    {
        try
        {
            _btnGuardar.Enabled = false;

            if (_category is null)
            {
                await _categoryService.CreateAsync(new CreateCategoryRequest(
                    _txtNombre.Text,
                    _txtDescripcion.Text));
            }
            else
            {
                await _categoryService.UpdateAsync(new UpdateCategoryRequest(
                    _category.CategoriaId,
                    _txtNombre.Text,
                    _txtDescripcion.Text));
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Categorias", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed)
            {
                _btnGuardar.Enabled = true;
            }
        }
    }

    private static bool IsDefaultCategory(string categoryName)
    {
        return string.Equals(categoryName, "Sin categoria", StringComparison.OrdinalIgnoreCase);
    }
}
