using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

internal sealed class CustomerSalePickerForm : Form
{
    private readonly ICustomerQueryService _customers;
    private readonly TextBox _search = new();
    private readonly Button _clearSearch = new();
    private readonly DataGridView _grid = new();
    private readonly BindingSource _binding = new();
    private readonly Button _select = new();
    private LiveSearchController? _liveSearch;

    public CustomerSalePickerForm(ICustomerQueryService customers)
    {
        _customers = customers;
        BuildUi();
        _liveSearch = new LiveSearchController(
            _search,
            _clearSearch,
            LoadCustomersAsync,
            ex => MessageBox.Show(ex.Message, "Seleccionar cliente", MessageBoxButtons.OK, MessageBoxIcon.Warning));
        Load += async (_, _) =>
        {
            await LoadCustomersAsync(CancellationToken.None);
            _search.Focus();
        };
    }

    public CustomerSaleSelection? SelectedCustomer { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _liveSearch?.Dispose();
            _binding.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        Text = "Seleccionar cliente";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 520);
        MinimumSize = new Size(680, 460);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(20),
            BackColor = UITheme.BackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.Controls.Add(new Label
        {
            Text = "Cliente de la venta\nBusque por código, nombre, teléfono o documento.",
            Dock = DockStyle.Fill,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.PrimaryColor,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        _search.Dock = DockStyle.Fill;
        _search.Margin = new Padding(0, 4, 8, 4);
        _search.PlaceholderText = "Escriba para buscar...";
        _search.Font = UITheme.NormalFont;
        UITheme.StyleTextBox(_search);
        _clearSearch.Text = "×";
        _clearSearch.Dock = DockStyle.Fill;
        _clearSearch.Margin = new Padding(0, 4, 0, 4);
        UITheme.StyleSecondaryButton(_clearSearch);
        searchRow.Controls.Add(_search, 0, 0);
        searchRow.Controls.Add(_clearSearch, 1, 0);
        root.Controls.Add(searchRow, 0, 1);

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.DataSource = _binding;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(CustomerSummary.CodigoCliente), HeaderText = "Código", Width = 120 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(CustomerSummary.NombreCompleto), HeaderText = "Cliente", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 220 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(CustomerSummary.TelefonoCelular), HeaderText = "Teléfono", Width = 130 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(CustomerSummary.DocumentoIdentidad), HeaderText = "Documento", Width = 140 });
        _grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                await SelectCurrentAsync();
            }
        };
        UITheme.StyleDataGridView(_grid);
        root.Controls.Add(_grid, 0, 2);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        var finalCustomer = new Button { Text = "Consumidor final", Dock = DockStyle.Fill, Margin = new Padding(0, 8, 8, 0) };
        finalCustomer.Click += (_, _) =>
        {
            SelectedCustomer = null;
            DialogResult = DialogResult.OK;
        };
        UITheme.StyleSecondaryButton(finalCustomer);
        actions.Controls.Add(finalCustomer, 1, 0);
        _select.Text = "Seleccionar cliente";
        _select.Dock = DockStyle.Fill;
        _select.Margin = new Padding(8, 8, 0, 0);
        _select.Click += async (_, _) => await SelectCurrentAsync();
        UITheme.StylePrimaryButton(_select);
        actions.Controls.Add(_select, 2, 0);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);
    }

    private async Task LoadCustomersAsync(CancellationToken cancellationToken)
    {
        var rows = await _customers.SearchAsync(
            new CustomerFilter(_search.Text.Trim(), EstadoCliente.ACTIVO, 1, 100),
            cancellationToken);
        if (!cancellationToken.IsCancellationRequested && !IsDisposed)
        {
            _binding.DataSource = rows;
        }
    }

    private async Task SelectCurrentAsync()
    {
        if (_grid.CurrentRow?.DataBoundItem is not CustomerSummary row)
        {
            MessageBox.Show("Seleccione un cliente de la lista.", "Seleccionar cliente", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selection = await _customers.GetSaleSelectionAsync(row.ClienteId);
        if (selection is null || !selection.Activo)
        {
            MessageBox.Show("El cliente seleccionado ya no está activo.", "Seleccionar cliente", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            await LoadCustomersAsync(CancellationToken.None);
            return;
        }

        SelectedCustomer = selection;
        DialogResult = DialogResult.OK;
    }
}
