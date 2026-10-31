using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

public class ExcelImportForm : Form
{
    private readonly IProductImportService _productImportService;
    private readonly BindingSource _bindingSource = new();

    private readonly Button _btnTemplate = new();
    private readonly Button _btnSelectFile = new();
    private readonly Button _btnImport = new();
    private readonly Label _lblFile = new();
    private readonly Label _lblSummary = new();
    private readonly DataGridView _grid = new();

    private ProductImportPreviewResult? _preview;

    public ExcelImportForm(IProductImportService productImportService)
    {
        _productImportService = productImportService;
        BuildUi();
    }

    private void BuildUi()
    {
        Text = "Importar productos desde Excel";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1120, 640);
        MinimumSize = new Size(960, 520);
        UITheme.StyleForm(this);

        ConfigureButton(_btnTemplate, "1 · Generar plantilla", BtnTemplate_Click, UITheme.StyleOutlineButton, 190);
        ConfigureButton(_btnSelectFile, "2 · Seleccionar archivo .xlsx", BtnSelectFile_Click, UITheme.StylePrimaryButton, 245);
        ConfigureButton(_btnImport, "3 · Importar filas válidas", BtnImport_Click, UITheme.StyleSuccessButton, 225);
        _btnImport.Enabled = false;

        _lblFile.Text = "Archivo: pendiente";
        _lblFile.Dock = DockStyle.Fill;
        _lblFile.AutoEllipsis = true;
        UITheme.StyleStatusLabel(_lblFile, UITheme.SecondaryColor);

        _lblSummary.Text = "Resumen: pendiente";
        _lblSummary.Dock = DockStyle.Fill;
        _lblSummary.AutoEllipsis = true;
        UITheme.StyleStatusLabel(_lblSummary);

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
        UITheme.StyleDataGridView(_grid);

        AddGridColumn("RowNumber", "Fila", 48);
        AddGridColumn("Codigo", "Codigo", 88);
        AddGridColumn("Nombre", "Nombre", 135);
        AddGridColumn("Categoria", "Categoria", 110);
        AddGridColumn("PrecioCompra", "Precio compra", 88, numeric: true);
        AddGridColumn("PrecioVenta", "Precio venta", 88, numeric: true);
        AddGridColumn("StockActual", "Stock", 62, numeric: true);
        AddGridColumn("StockMinimo", "Stock mínimo", 82, numeric: true);
        AddGridColumn("Estado", "Estado", 78);
        AddGridColumn("Observaciones", "Observaciones", 120);
        AddGridColumn("Error", "Error", 120);
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        var workflow = VisualLayout.CreateActionBar(_btnTemplate, _btnSelectFile, _btnImport);
        var status = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UITheme.SurfaceColor
        };
        status.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        status.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        status.Controls.Add(_lblFile, 0, 0);
        status.Controls.Add(_lblSummary, 0, 1);
        var gridHost = VisualLayout.CreateSection(
            "Vista previa de importación",
            "Solo se habilita la importación cuando existen filas válidas. Los errores permanecen visibles por fila.",
            _grid);

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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(VisualLayout.CreatePageHeader(
            "Importar productos desde Excel",
            "Genera la plantilla, revisa la vista previa y confirma únicamente las filas válidas."), 0, 0);
        root.Controls.Add(workflow, 0, 1);
        root.Controls.Add(status, 0, 2);
        root.Controls.Add(gridHost, 0, 3);
        Controls.Add(root);
    }

    private async void BtnTemplate_Click(object? sender, EventArgs e)
    {
        try
        {
            SetActionsEnabled(false);
            var path = await _productImportService.CreateProductImportTemplateAsync();
            MessageBox.Show(
                $"Plantilla generada:\n\n{path}",
                "Importar Excel",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Importar Excel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetActionsEnabled(true);
        }
    }

    private async void BtnSelectFile_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Seleccionar plantilla de productos",
            Filter = "Archivos Excel (*.xlsx)|*.xlsx",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await LoadPreviewAsync(dialog.FileName);
    }

    private async void BtnImport_Click(object? sender, EventArgs e)
    {
        if (_preview is null || !_preview.CanImport)
        {
            MessageBox.Show(
                "No hay filas validas para importar.",
                "Importar Excel",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Importar {_preview.FilasValidas} producto(s) validos?\n\nSe creara un respaldo antes de importar.",
            "Importar Excel",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        try
        {
            SetActionsEnabled(false);
            var result = await _productImportService.ImportProductsAsync(_preview.FilePath);

            MessageBox.Show(
                $"Importacion finalizada.\n\nFilas leidas: {result.FilasLeidas}\nProductos importados: {result.ProductosImportados}\nFilas con error: {result.FilasConError}\nDuplicados ignorados: {result.DuplicadosIgnorados}\n\nRespaldo:\n{result.BackupPath}",
                "Importar Excel",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            await LoadPreviewAsync(_preview.FilePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"No se pudo importar:\n\n{ex.Message}",
                "Importar Excel",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            SetActionsEnabled(true);
        }
    }

    private async Task LoadPreviewAsync(string filePath)
    {
        try
        {
            SetActionsEnabled(false);

            _preview = await _productImportService.PreviewProductsAsync(filePath);
            _lblFile.Text = $"Archivo: {_preview.FilePath}";

            if (_preview.GlobalErrors.Count > 0)
            {
                _lblSummary.Text = "Resumen: plantilla invalida";
                _bindingSource.DataSource = Array.Empty<ProductImportPreviewRow>();
                _btnImport.Enabled = false;

                MessageBox.Show(
                    string.Join(Environment.NewLine, _preview.GlobalErrors),
                    "Importar Excel",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _bindingSource.DataSource = _preview.Rows.ToList();
            RenderSummary();
        }
        catch (Exception ex)
        {
            _preview = null;
            _bindingSource.DataSource = Array.Empty<ProductImportPreviewRow>();
            _lblFile.Text = "Archivo: pendiente";
            _lblSummary.Text = "Resumen: pendiente";
            _btnImport.Enabled = false;

            MessageBox.Show(ex.Message, "Importar Excel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetActionsEnabled(true);
        }
    }

    private void RenderSummary()
    {
        if (_preview is null)
        {
            _lblSummary.Text = "Resumen: pendiente";
            _btnImport.Enabled = false;
            return;
        }

        _lblSummary.Text =
            $"Resumen: filas leidas {_preview.FilasLeidas} | validas {_preview.FilasValidas} | errores {_preview.FilasConError} | duplicados {_preview.Duplicados}";
        _btnImport.Enabled = _preview.CanImport;
    }

    private void SetActionsEnabled(bool enabled)
    {
        _btnTemplate.Enabled = enabled;
        _btnSelectFile.Enabled = enabled;
        _btnImport.Enabled = enabled && _preview?.CanImport == true;
    }

    private void AddGridColumn(string propertyName, string headerText, int width, bool numeric = false)
    {
        var column = new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = headerText,
            Width = width,
            MinimumWidth = width,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = width
        };

        if (numeric)
        {
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }

        if (propertyName is "PrecioCompra" or "PrecioVenta")
        {
            column.DefaultCellStyle.Format = "0.00";
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
        UITheme.EnsureButtonContentFits(button, minimumWidth, 42, 36);
        button.Size = button.MinimumSize;
    }
}
