using System.ComponentModel;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

internal sealed class GenerateMissingBarcodesForm : Form
{
    private readonly IBarcodeService _service;
    private readonly BindingList<GenerationRow> _rows;
    private readonly DataGridView _grid = new();
    private readonly Label _summary = new();
    private readonly Button _generate = new();

    public GenerateMissingBarcodesForm(
        IBarcodeService service,
        IReadOnlyList<BarcodeSkuCandidate> candidates)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        ArgumentNullException.ThrowIfNull(candidates);
        _rows = new BindingList<GenerationRow>(candidates
            .Where(candidate => candidate.EsElegibleParaGeneracionInterna)
            .Select(candidate => new GenerationRow(candidate))
            .ToList());
        BuildUi();
        UpdateSummary();
    }

    private void BuildUi()
    {
        Text = "Generar códigos faltantes";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(950, 620);
        MinimumSize = new Size(780, 500);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 4, BackColor = UITheme.BackgroundColor };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.Controls.Add(new Label
        {
            Text = "Generar códigos faltantes\nSolo se incluyen SKU activos sin barcode actual ni historial inactivo.",
            Dock = DockStyle.Fill,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        ConfigureGrid();
        root.Controls.Add(_grid, 0, 1);
        _summary.Dock = DockStyle.Fill; _summary.Font = UITheme.NormalFont; _summary.ForeColor = UITheme.MutedTextColor;
        _summary.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(_summary, 0, 2);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var close = new Button { Text = "Cerrar", Size = new Size(120, 40), DialogResult = DialogResult.Cancel };
        _generate.Text = "Generar códigos"; _generate.Size = new Size(175, 40);
        var clear = new Button { Text = "Deseleccionar", Size = new Size(145, 40) };
        var selectAll = new Button { Text = "Seleccionar todos", Size = new Size(180, 40) };
        UITheme.StylePrimaryButton(_generate); UITheme.StyleSecondaryButton(close); UITheme.StyleOutlineButton(clear); UITheme.StyleOutlineButton(selectAll);
        _generate.Click += async (_, _) => await GenerateAsync();
        clear.Click += (_, _) => SetSelected(false);
        selectAll.Click += (_, _) => SetSelected(true);
        actions.Controls.AddRange([close, _generate, clear, selectAll]);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = false;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        UITheme.StyleDataGridView(_grid);
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(GenerationRow.Selected), HeaderText = "Generar", Width = 70 });
        AddTextColumn("Producto", nameof(GenerationRow.Producto), 260, fill: true);
        AddTextColumn("SKU", nameof(GenerationRow.Sku), 170);
        AddTextColumn("Variante", nameof(GenerationRow.Variante), 150);
        AddTextColumn("Barcode actual", nameof(GenerationRow.BarcodeActual), 145);
        AddTextColumn("Resultado", nameof(GenerationRow.Resultado), 230);
        _grid.DataSource = _rows;
        _grid.CellValueChanged += (_, _) => UpdateSummary();
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
    }

    private async Task GenerateAsync()
    {
        var selected = _rows.Where(row => row.Selected && row.CanGenerate).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Seleccione al menos un SKU elegible.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirmation = MessageBox.Show(
            $"Se generarán {selected.Count} códigos internos Code 128.\n\nNo se modificarán códigos existentes.",
            Text,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirmation != DialogResult.Yes) return;

        SetBusy(true);
        try
        {
            var result = await _service.GenerateMissingBarcodesAsync(
                new GenerateMissingBarcodesRequest(selected.Select(row => row.Candidate.ProductoSkuId).ToArray()));
            foreach (var item in result.Items)
            {
                var row = _rows.SingleOrDefault(current => current.Candidate.ProductoSkuId == item.Candidate.ProductoSkuId);
                if (row is not null) row.Apply(item);
            }
            _grid.Refresh();
            _summary.Text = $"Generados: {result.GeneratedCount}   Omitidos: {result.OmittedCount}   Errores: {result.ErrorCount}";
            if (result.ErrorCount > 0)
            {
                MessageBox.Show("La operación terminó con errores revisables en la tabla.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { SetBusy(false); }
    }

    private void SetSelected(bool selected)
    {
        foreach (var row in _rows.Where(row => row.CanGenerate)) row.Selected = selected;
        _grid.Refresh(); UpdateSummary();
    }

    private void UpdateSummary()
    {
        var count = _rows.Count(row => row.Selected && row.CanGenerate);
        _summary.Text = _rows.Count == 0
            ? "No hay SKU elegibles. Los códigos activos e históricos se preservan."
            : $"{_rows.Count} SKU elegibles · {count} seleccionados.";
        _generate.Enabled = count > 0;
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        _generate.Enabled = !busy;
        _grid.Enabled = !busy;
    }

    private void AddTextColumn(string header, string property, int width, bool fill = false) =>
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            DataPropertyName = property,
            Width = width,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
            ReadOnly = true
        });

    private sealed class GenerationRow
    {
        public GenerationRow(BarcodeSkuCandidate candidate) => Candidate = candidate;
        public BarcodeSkuCandidate Candidate { get; }
        public bool Selected { get; set; } = true;
        public string Producto => Candidate.ProductoNombre;
        public string Sku => Candidate.CodigoSku;
        public string Variante => Candidate.NombreVariante ?? "-";
        public string BarcodeActual => Candidate.TieneBarcodeActivo ? Candidate.BarcodeActivo! : "Ninguno";
        public string Resultado { get; private set; } = "Pendiente";
        public bool CanGenerate => Candidate.EsElegibleParaGeneracionInterna && Resultado == "Pendiente";

        public void Apply(MissingBarcodeGenerationItem item)
        {
            Selected = false;
            Resultado = item.Status switch
            {
                MissingBarcodeGenerationStatus.Generated => $"Generado: {item.Barcode}",
                MissingBarcodeGenerationStatus.Omitted => $"Omitido: {item.Detail}",
                _ => $"Error: {item.Detail}"
            };
        }
    }
}

internal sealed class LabelBatchSelectionForm : Form
{
    private readonly BindingList<LabelRow> _rows;
    private readonly DataGridView _grid = new();
    private readonly ComboBox _profile = new();
    private readonly CheckBox _includePrice = new() { Text = "Incluir precio", Checked = true, AutoSize = true };
    private readonly Label _summary = new();

    public LabelBatchSelectionForm(IReadOnlyList<BarcodeSkuCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        _rows = new BindingList<LabelRow>(candidates.Select(candidate => new LabelRow(candidate)).ToList());
        BuildUi();
        UpdateSummary();
    }

    public LabelBatchPreview? Batch { get; private set; }

    private void BuildUi()
    {
        Text = "Etiquetas en lote";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1030, 660);
        MinimumSize = new Size(820, 530);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 5, BackColor = UITheme.BackgroundColor };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.Controls.Add(new Label
        {
            Text = "Etiquetas en lote\nSelecciona SKU y cantidad. Los SKU sin barcode se informan; nunca se genera uno al imprimir.",
            Dock = DockStyle.Fill,
            Font = UITheme.SubheaderFont,
            ForeColor = UITheme.TextDark,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        root.Controls.Add(BuildOptions(), 0, 1);
        ConfigureGrid(); root.Controls.Add(_grid, 0, 2);
        _summary.Dock = DockStyle.Fill; _summary.Font = UITheme.NormalFont; _summary.ForeColor = UITheme.MutedTextColor; _summary.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(_summary, 0, 3);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var close = new Button { Text = "Cerrar", Size = new Size(120, 40), DialogResult = DialogResult.Cancel };
        var preview = new Button { Text = "Ver vista previa", Size = new Size(180, 40) };
        var deselect = new Button { Text = "Deseleccionar", Size = new Size(145, 40) };
        var select = new Button { Text = "Seleccionar con barcode", Size = new Size(240, 40) };
        UITheme.StyleSecondaryButton(close); UITheme.StylePrimaryButton(preview); UITheme.StyleOutlineButton(deselect); UITheme.StyleOutlineButton(select);
        preview.Click += (_, _) => PrepareBatch();
        deselect.Click += (_, _) => SetSelected(false, onlyWithBarcode: false);
        select.Click += (_, _) => SetSelected(true, onlyWithBarcode: true);
        actions.Controls.AddRange([close, preview, deselect, select]);
        root.Controls.Add(actions, 0, 4);
        Controls.Add(root);
    }

    private Control BuildOptions()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 5, 0, 0) };
        panel.Controls.Add(new Label { Text = "Perfil:", AutoSize = true, Font = UITheme.NormalBoldFont, Margin = new Padding(4, 9, 8, 0) });
        _profile.DataSource = ProductLabelProfiles.All.ToList();
        _profile.DisplayMember = nameof(ProductLabelProfileDefinition.DisplayName);
        _profile.ValueMember = nameof(ProductLabelProfileDefinition.Profile);
        // El binding del ComboBox se materializa cuando el control ya tiene
        // padre. Elegir 40×30 en Load evita SelectedIndex fuera de rango en
        // WinForms/DPI y conserva el perfil operativo predeterminado.
        Load += (_, _) =>
        {
            if (_profile.Items.Count > 1) _profile.SelectedIndex = 1;
        };
        _profile.Width = 160; UITheme.StyleInput(_profile);
        panel.Controls.Add(_profile);
        _includePrice.Margin = new Padding(18, 8, 0, 0); _includePrice.Font = UITheme.NormalFont;
        panel.Controls.Add(_includePrice);
        return panel;
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = false;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        UITheme.StyleDataGridView(_grid);
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(LabelRow.Selected), HeaderText = "Usar", Width = 58 });
        AddTextColumn("Producto", nameof(LabelRow.Producto), 255, fill: true);
        AddTextColumn("SKU", nameof(LabelRow.Sku), 155);
        AddTextColumn("Variante", nameof(LabelRow.Variante), 145);
        AddTextColumn("Barcode", nameof(LabelRow.Barcode), 250);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(LabelRow.Quantity), HeaderText = "Cantidad", Width = 78 });
        _grid.DataSource = _rows;
        _grid.CellValueChanged += (_, _) => UpdateSummary();
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
    }

    private void PrepareBatch()
    {
        var selected = _rows.Where(row => row.Selected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Seleccione al menos un SKU.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var missing = selected.Where(row => !row.Candidate.TieneBarcodeActivo).ToList();
        if (missing.Count > 0)
        {
            MessageBox.Show(
                $"{missing.Count} SKU seleccionado(s) no tiene(n) código de barras activo.\n\n" +
                "No se generará ningún código durante la impresión. Use “Generar códigos faltantes” y vuelva a preparar el lote.",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            var profile = SelectedProfile.Profile;
            Batch = LabelDataFactory.CreateBatch(
                selected.Select(row => (row.Candidate, row.GetQuantity())),
                profile,
                _includePrice.Checked);
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SetSelected(bool selected, bool onlyWithBarcode)
    {
        foreach (var row in _rows)
        {
            row.Selected = selected && (!onlyWithBarcode || row.Candidate.TieneBarcodeActivo);
        }
        _grid.Refresh(); UpdateSummary();
    }

    private void UpdateSummary()
    {
        var selected = _rows.Where(row => row.Selected).ToList();
        _summary.Text = selected.Count == 0
            ? "Selecciona SKU con barcode activo para preparar el lote."
            : $"{selected.Count} SKU seleccionados · {selected.Count(row => !row.Candidate.TieneBarcodeActivo)} sin barcode activo.";
    }

    private ProductLabelProfileDefinition SelectedProfile =>
        _profile.SelectedItem as ProductLabelProfileDefinition ??
        ProductLabelProfiles.Get(ProductLabelProfile.Mm40x30);

    private void AddTextColumn(string header, string property, int width, bool fill = false) =>
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            DataPropertyName = property,
            Width = width,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
            ReadOnly = true
        });

    private sealed class LabelRow
    {
        public LabelRow(BarcodeSkuCandidate candidate) => Candidate = candidate;
        public BarcodeSkuCandidate Candidate { get; }
        public bool Selected { get; set; }
        public string Producto => Candidate.ProductoNombre;
        public string Sku => Candidate.CodigoSku;
        public string Variante => Candidate.NombreVariante ?? "-";
        public string Barcode => Candidate.BarcodeActivo ?? "Sin código de barras";
        public string Quantity { get; set; } = "1";

        public int GetQuantity()
        {
            if (!int.TryParse(Quantity, out var value) || value is < 1 or > ProductLabelProfiles.MaxCopiesPerSku)
            {
                throw new InvalidOperationException(
                    $"La cantidad para '{Sku}' debe estar entre 1 y {ProductLabelProfiles.MaxCopiesPerSku}.");
            }
            return value;
        }
    }
}
