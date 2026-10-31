using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

public class CustomerDuplicateWarningForm : Form
{
    private readonly CustomerDuplicateCheckResult _result;
    private readonly BindingSource _binding = new();
    private readonly DataGridView _grid = new();
    private readonly Button _btnOpen = new();
    private readonly Button _btnContinue = new();
    private readonly Button _btnCancel = new();

    public CustomerDuplicateWarningForm(CustomerDuplicateCheckResult result)
    {
        _result = result;
        SelectedCustomerId = result.Candidates.FirstOrDefault()?.ClienteId;
        BuildUi();
    }

    public int? SelectedCustomerId { get; private set; }

    public bool ContinueConfirmed { get; private set; }

    public bool OpenSelectedRequested { get; private set; }

    private void BuildUi()
    {
        Text = "Posibles clientes duplicados";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 430);
        MinimumSize = new Size(680, 360);
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var lblMessage = new Label
        {
            Text = _result.HasBlockingDuplicate
                ? "Ya existe un cliente con el mismo DUI o documento. Revise el registro existente."
                : "Se encontraron coincidencias. Revise antes de continuar.",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12)
        };
        UITheme.StyleLabelHeader(lblMessage);

        ConfigureGrid();
        _binding.DataSource = _result.Candidates
            .Select(CustomerDuplicateRow.FromCandidate)
            .ToList();
        _grid.DataSource = _binding;
        _grid.SelectionChanged += (_, _) => UpdateSelection();
        _grid.CellDoubleClick += (_, _) => OpenSelected();

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };

        ConfigureButton(_btnCancel, "Cancelar", 110, (_, _) => DialogResult = DialogResult.Cancel);
        UITheme.StyleSecondaryButton(_btnCancel);

        ConfigureButton(_btnContinue, "Continuar", 120, (_, _) =>
        {
            ContinueConfirmed = true;
            DialogResult = DialogResult.OK;
        });
        UITheme.StylePrimaryButton(_btnContinue);
        _btnContinue.Visible = !_result.HasBlockingDuplicate;

        ConfigureButton(_btnOpen, "Abrir cliente", 130, (_, _) => OpenSelected());
        UITheme.StyleSecondaryButton(_btnOpen);

        actions.Controls.AddRange([_btnCancel, _btnContinue, _btnOpen]);

        root.Controls.Add(lblMessage, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
    }

    private void ConfigureGrid()
    {
        UITheme.StyleDataGridView(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;

        AddGridColumn("CodigoCliente", "Codigo", 90, 12);
        AddGridColumn("NombreCompleto", "Nombre completo", 210, 30);
        AddGridColumn("TelefonoCelular", "Telefono", 115, 16);
        AddGridColumn("DocumentoIdentidad", "DUI/documento", 120, 16);
        AddGridColumn("Estado", "Estado", 85, 10);
        AddGridColumn("Coincidencia", "Coincidencia", 120, 16);
        AddGridColumn("Regla", "Regla", 110, 14);
    }

    private void AddGridColumn(string property, string header, int minimumWidth, float fillWeight)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            MinimumWidth = minimumWidth,
            FillWeight = fillWeight,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
    }

    private void UpdateSelection()
    {
        if (_binding.Current is CustomerDuplicateRow row)
        {
            SelectedCustomerId = row.ClienteId;
        }
    }

    private void OpenSelected()
    {
        UpdateSelection();
        if (SelectedCustomerId is null)
        {
            return;
        }

        OpenSelectedRequested = true;
        DialogResult = DialogResult.Retry;
    }

    private static void ConfigureButton(Button button, string text, int width, EventHandler handler)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 36;
        button.Margin = new Padding(8, 0, 0, 0);
        button.Click += handler;
    }

    private sealed record CustomerDuplicateRow(
        int ClienteId,
        string CodigoCliente,
        string NombreCompleto,
        string TelefonoCelular,
        string DocumentoIdentidad,
        string Estado,
        string Coincidencia,
        string Regla)
    {
        public static CustomerDuplicateRow FromCandidate(CustomerDuplicateCandidate candidate)
        {
            return new CustomerDuplicateRow(
                candidate.ClienteId,
                candidate.CodigoCliente,
                candidate.NombreCompleto,
                candidate.TelefonoCelular ?? string.Empty,
                candidate.DocumentoIdentidad ?? string.Empty,
                candidate.Estado.ToString(),
                candidate.Kind switch
                {
                    CustomerDuplicateKind.Documento => "DUI identico",
                    CustomerDuplicateKind.Telefono => "Telefono identico",
                    CustomerDuplicateKind.Correo => "Correo identico",
                    _ => "Nombre parecido"
                },
                candidate.Blocking ? "Bloquea" : "Advertencia");
        }
    }
}
