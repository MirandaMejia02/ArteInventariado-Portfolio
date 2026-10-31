using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

internal static class ProductSubcategorySmokeRunner
{
    public static int Run(string? outputDirectory)
    {
        outputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(Path.GetTempPath(), "ArteInventariado.ProductSubcategorySmoke")
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var log = new List<string> { $"START={DateTimeOffset.UtcNow:O}" };

        try
        {
            var fixture = new SmokeFixture();
            foreach (var scale in new[] { 1f, 1.25f, 1.5f })
            {
                SmokeCreateForm(fixture, scale, outputDirectory, log);
                SmokeEditForm(fixture, scale, outputDirectory, log);
                SmokeInactiveEditForm(fixture, scale, outputDirectory, log);
                foreach (var clientSize in new[] { new Size(980, 620), new Size(1280, 720), new Size(1920, 1080) })
                {
                    SmokeProductsList(fixture, scale, clientSize, outputDirectory, log);
                }
                SmokeProductWorkspace(fixture, scale, outputDirectory, log);
            }

            log.Add("RESULT=OK");
            return 0;
        }
        catch (Exception ex)
        {
            log.Add("RESULT=FAILED");
            log.Add(ex.ToString());
            return 1;
        }
        finally
        {
            log.Add($"END={DateTimeOffset.UtcNow:O}");
            File.WriteAllLines(Path.Combine(outputDirectory, "product-subcategory-smoke.log"), log);
        }
    }

    private static void SmokeCreateForm(
        SmokeFixture fixture,
        float scale,
        string outputDirectory,
        ICollection<string> log)
    {
        var products = new SmokeProductService();
        using var form = new ProductEditForm(
            products,
            fixture.Categories,
            product: null,
            loadDataOnShow: false,
            subcategoryService: fixture.Subcategories);
        form.LoadForSmokeAsync().GetAwaiter().GetResult();

        Assert(ReferenceEquals(form.EscapeCancelButtonForSmoke, form.CancelButtonForSmoke),
            "Crear: Escape debe activar Cancelar, nunca Guardar.");
        Assert(form.SubcategoryOptionsForSmoke.SequenceEqual(["Sin subcategoría", "Ollas"]),
            "Crear: la categoría con hijos debe mostrar Sin subcategoría y Ollas.");
        Assert(form.SelectedSubcategoryIdForSmoke is null,
            "Crear: no debe seleccionarse automáticamente una subcategoría real.");
        Assert(form.InitialLocationOptionsForSmoke.SequenceEqual(["Sin asignar", "Exhibición", "Bodega"]),
            "Crear: la ubicación inicial debe ofrecer Sin asignar, Exhibición y Bodega.");
        Assert(form.SelectedInitialLocationForSmoke == UbicacionInventario.SIN_ASIGNAR,
            "Crear: la ubicación inicial predeterminada debe ser Sin asignar.");
        Assert(form.InitialLocationSelectorForSmoke.Enabled,
            "Crear: el selector de ubicación inicial debe estar habilitado.");
        Capture(form, scale, Path.Combine(outputDirectory, $"product-create-{ScaleName(scale)}.png"));
        log.Add($"ProductEditForm crear selector dependiente y Escape cancela @{scale:P0}=OK");

        SmokeInitialLocationSelection(fixture, scale, log);

        form.SelectCategoryForSmokeAsync(SmokeFixture.NoChildrenCategoryId).GetAwaiter().GetResult();
        Assert(form.SubcategoryOptionsForSmoke.SequenceEqual(["Sin subcategoría"]),
            "Crear: una categoría sin hijos debe mostrar solo Sin subcategoría.");
        Assert(form.SelectedSubcategoryIdForSmoke is null,
            "Crear: cambiar de categoría debe limpiar la subcategoría.");
        Capture(form, scale, Path.Combine(outputDirectory, $"product-create-no-subcategories-{ScaleName(scale)}.png"));

        form.SelectCategoryForSmokeAsync(SmokeFixture.BarroCategoryId).GetAwaiter().GetResult();
        form.SubcategorySelectorForSmoke.SelectedIndex = 1;
        form.NameInputForSmoke.Text = "Olla de smoke";
        form.SetRequiredValuesForSmoke();
        form.SaveForSmokeAsync().GetAwaiter().GetResult();
        Assert(products.LastCreate?.SubcategoriaId == SmokeFixture.OllasSubcategoryId,
            "Crear: no se entregó el ID de la subcategoría elegida.");
        log.Add("ProductEditForm crear con subcategoría compatible=OK");

        var without = new SmokeProductService();
        using var withoutForm = new ProductEditForm(
            without,
            fixture.Categories,
            product: null,
            loadDataOnShow: false,
            subcategoryService: fixture.Subcategories);
        withoutForm.LoadForSmokeAsync().GetAwaiter().GetResult();
        withoutForm.NameInputForSmoke.Text = "Producto sin subcategoría";
        withoutForm.SetRequiredValuesForSmoke();
        withoutForm.ConfirmUnassignedInitialStockForSmoke = () =>
            throw new InvalidOperationException("No debe pedir confirmación si no hay cantidad inicial.");
        withoutForm.SaveForSmokeAsync().GetAwaiter().GetResult();
        Assert(without.LastCreate?.SubcategoriaId is null,
            "Crear: Sin subcategoría debe entregar null.");
        log.Add("ProductEditForm crear sin subcategoría=null=OK");
    }

    private static void SmokeEditForm(
        SmokeFixture fixture,
        float scale,
        string outputDirectory,
        ICollection<string> log)
    {
        var product = fixture.Product(
            name: "Producto con Ollas",
            categoryId: SmokeFixture.BarroCategoryId,
            subcategoryId: SmokeFixture.OllasSubcategoryId,
            subcategoryName: "Ollas");
        using var form = new ProductEditForm(
            new SmokeProductService(),
            fixture.Categories,
            product,
            loadDataOnShow: false,
            subcategoryService: fixture.Subcategories);
        form.LoadForSmokeAsync().GetAwaiter().GetResult();

        Assert(form.SelectedSubcategoryIdForSmoke == SmokeFixture.OllasSubcategoryId,
            "Editar: no cargó la subcategoría actual compatible.");
        Assert(!form.InitialLocationSelectorForSmoke.Enabled && !form.InitialLocationSelectorForSmoke.TabStop,
            "Editar: la ubicación inicial no debe estar disponible ni provocar movimientos.");
        Capture(form, scale, Path.Combine(outputDirectory, $"product-edit-{ScaleName(scale)}.png"));
        log.Add($"ProductEditForm editar con subcategoría @{scale:P0}=OK");

        form.SelectCategoryForSmokeAsync(SmokeFixture.NoChildrenCategoryId).GetAwaiter().GetResult();
        Assert(form.SelectedSubcategoryIdForSmoke is null,
            "Editar: cambiar de categoría no debe conservar un ID incompatible.");
    }

    private static void SmokeInitialLocationSelection(
        SmokeFixture fixture,
        float scale,
        ICollection<string> log)
    {
        var options = new[]
        {
            (Index: 0, Location: UbicacionInventario.SIN_ASIGNAR),
            (Index: 1, Location: UbicacionInventario.EXHIBICION),
            (Index: 2, Location: UbicacionInventario.BODEGA)
        };

        foreach (var option in options)
        {
            var products = new SmokeProductService();
            using var form = new ProductEditForm(
                products,
                fixture.Categories,
                product: null,
                loadDataOnShow: false,
                subcategoryService: fixture.Subcategories);
            form.LoadForSmokeAsync().GetAwaiter().GetResult();
            form.NameInputForSmoke.Text = $"Producto ubicación {option.Location}";
            form.SetRequiredValuesForSmoke(initialStock: 4);
            form.InitialLocationSelectorForSmoke.SelectedIndex = option.Index;
            if (option.Location == UbicacionInventario.SIN_ASIGNAR)
            {
                form.ConfirmUnassignedInitialStockForSmoke = () => true;
            }
            Assert(form.SelectedInitialLocationForSmoke == option.Location,
                $"Crear: no se seleccionó {option.Location} en el formulario.");

            form.SaveForSmokeAsync().GetAwaiter().GetResult();
            Assert(products.LastCreate?.UbicacionInicial == option.Location,
                $"Crear: no se entregó {option.Location} al servicio.");
        }

        var rejectedProducts = new SmokeProductService();
        using var rejectedForm = new ProductEditForm(
            rejectedProducts,
            fixture.Categories,
            product: null,
            loadDataOnShow: false,
            subcategoryService: fixture.Subcategories);
        rejectedForm.LoadForSmokeAsync().GetAwaiter().GetResult();
        rejectedForm.NameInputForSmoke.Text = "Producto requiere ubicación física";
        rejectedForm.SetRequiredValuesForSmoke(initialStock: 4);
        var promptCount = 0;
        rejectedForm.ConfirmUnassignedInitialStockForSmoke = () =>
        {
            promptCount++;
            return false;
        };
        rejectedForm.SaveForSmokeAsync().GetAwaiter().GetResult();
        Assert(promptCount == 1 && rejectedProducts.LastCreate is null,
            "Crear: rechazar SIN_ASIGNAR debe detener el guardado y permitir elegir ubicación.");
        rejectedForm.InitialLocationSelectorForSmoke.SelectedIndex = 2;
        rejectedForm.SaveForSmokeAsync().GetAwaiter().GetResult();
        Assert(promptCount == 1 && rejectedProducts.LastCreate?.UbicacionInicial == UbicacionInventario.BODEGA,
            "Crear: tras elegir ubicación física, debe guardar sin volver a confirmar SIN_ASIGNAR.");

        log.Add($"ProductEditForm ubicación inicial SIN_ASIGNAR/EXHIBICION/BODEGA y confirmación @{scale:P0}=OK");
    }

    private static void SmokeInactiveEditForm(
        SmokeFixture fixture,
        float scale,
        string outputDirectory,
        ICollection<string> log)
    {
        var products = new SmokeProductService();
        var product = fixture.Product(
            name: "Producto histórico",
            categoryId: SmokeFixture.BarroCategoryId,
            subcategoryId: SmokeFixture.InactiveSubcategoryId,
            subcategoryName: "Tazas");
        using var form = new ProductEditForm(
            products,
            fixture.Categories,
            product,
            loadDataOnShow: false,
            subcategoryService: fixture.Subcategories);
        form.LoadForSmokeAsync().GetAwaiter().GetResult();

        Assert(form.SelectedSubcategoryIdForSmoke == SmokeFixture.InactiveSubcategoryId,
            "Editar: no conservó la subcategoría histórica inactiva.");
        Assert(form.SubcategoryOptionsForSmoke.Any(text => text.Contains("inactiva", StringComparison.OrdinalIgnoreCase)),
            "Editar: la subcategoría histórica no fue identificada como inactiva.");
        Capture(form, scale, Path.Combine(outputDirectory, $"product-edit-inactive-{ScaleName(scale)}.png"));
        log.Add($"ProductEditForm editar asignación histórica inactiva @{scale:P0}=OK");
    }

    private static void SmokeProductsList(
        SmokeFixture fixture,
        float scale,
        Size clientSize,
        string outputDirectory,
        ICollection<string> log)
    {
        using var form = new ProductsForm(
            null!, null!, null!, null!, new SmokeProductAssetService(), null!, null!, null!,
            new SmokeInstallationContext(), loadDataOnShow: false)
        {
            WindowState = FormWindowState.Normal,
            ClientSize = clientSize
        };
        // ProductsForm has a production minimum size; the harness must set the
        // requested viewport after switching back from its maximized default.
        form.MinimumSize = Size.Empty;
        form.MaximumSize = Size.Empty;
        // A top-level window is constrained by the validation desktop work area
        // (which can be smaller than 1280x720). Host the form as a control so
        // the smoke can exercise both requested logical viewports deterministically.
        form.TopLevel = false;
        form.FormBorderStyle = FormBorderStyle.None;
        form.ClientSize = clientSize;
        form.BindProductsForSmoke([
            fixture.Product("Producto Ollas", SmokeFixture.BarroCategoryId, SmokeFixture.OllasSubcategoryId, "Ollas"),
            fixture.Product("Producto sin clasificación", SmokeFixture.NoChildrenCategoryId, null, null)
        ]);
        var sizeName = SizeName(clientSize);
        Capture(form, scale,
            Path.Combine(outputDirectory, $"products-subcategory-{sizeName}-{ScaleName(scale)}.png"),
            visibleForm => ValidateProductsListLayout(visibleForm, clientSize, scale));

        var grid = form.GeneralGridForSmoke;
        Assert(grid.Columns.Cast<DataGridViewColumn>().Any(column =>
                column.DataPropertyName == "Subcategoria" && column.HeaderText == "Subcategoría"),
            "Listado: falta la columna Subcategoría.");
        Assert(grid.Rows.Count == 2, "Listado: no se muestran los dos productos del smoke.");
        var subcategoryColumn = grid.Columns.Cast<DataGridViewColumn>()
            .Single(column => column.DataPropertyName == "Subcategoria");
        Assert(grid.Rows.Cast<DataGridViewRow>().Any(row => Equals(row.Cells[subcategoryColumn.Index].Value, "Ollas")),
            "Listado: no se muestra el nombre de la subcategoría.");
        Assert(grid.Rows.Cast<DataGridViewRow>().Any(row => Equals(row.Cells[subcategoryColumn.Index].Value, "Sin subcategoría")),
            "Listado: el valor null no usa el texto de estado vacío.");
        log.Add($"ProductsForm listado categoría/subcategoría {sizeName} @{scale:P0}=OK");
    }

    private static void ValidateProductsListLayout(Form source, Size requestedClientSize, float scale)
    {
        if (source is not ProductsForm form)
            throw new InvalidOperationException("El smoke recibió un formulario de productos inesperado.");

        if (form.ClientSize.Width < requestedClientSize.Width || form.ClientSize.Height < requestedClientSize.Height)
        {
            throw new InvalidOperationException(
                $"ProductsForm: el viewport solicitado {requestedClientSize} quedó en {form.ClientSize} a {scale:P0} " +
                $"(Size={form.Size}, MinimumSize={form.MinimumSize}, MaximumSize={form.MaximumSize}, TopLevel={form.TopLevel}, WindowState={form.WindowState}).");
        }

        var filters = form.FiltersForSmoke;
        var actions = form.ActionsForSmoke;
        var root = form.RootForSmoke;
        var tabs = form.TabsForSmoke;
        var grid = form.GeneralGridForSmoke;
        form.PerformLayout();
        tabs.PerformLayout();
        tabs.SelectedTab?.PerformLayout();
        grid.Parent?.PerformLayout();

        EnsureContained(form, root, "layout raíz");
        EnsureContained(form, filters, "filtros");
        EnsureContained(form, actions, "acciones");
        EnsureContained(form, tabs, "pestañas");
        EnsureContained(form, grid, "grilla");
        if (grid.Parent is not null) EnsureContained(grid.Parent, grid, "grilla en su página");

        if (filters.Bounds.Bottom > actions.Bounds.Top)
            throw new InvalidOperationException($"ProductsForm: filtros y acciones se superponen a {scale:P0}.");
        if (actions.Bounds.Bottom > tabs.Bounds.Top)
            throw new InvalidOperationException($"ProductsForm: acciones y grilla se superponen a {scale:P0}.");

        var filterFields = filters.Controls.Cast<Control>().Where(control => control.Visible).ToList();
        if (filterFields.Count != 11)
            throw new InvalidOperationException($"ProductsForm: se esperaban once filtros visibles, llegaron {filterFields.Count}.");
        foreach (var field in filterFields)
        {
            EnsureContained(filters, field, $"filtro '{field.Controls.OfType<Label>().FirstOrDefault()?.Text ?? field.Name}'");
            foreach (var child in Descendants(field).Where(control => control.Visible))
                EnsureContained(field, child, $"control del filtro '{field.Controls.OfType<Label>().FirstOrDefault()?.Text ?? field.Name}'");
        }

        var expectedActions = new[]
        {
            "Nuevo producto", "Abrir ficha", "Activar", "Desactivar", "Eliminar",
            "Generar códigos faltantes", "Etiquetas en lote", "Imágenes en lote", "Actualizar"
        };
        var buttons = actions.Controls.OfType<Button>().Where(button => button.Visible).ToList();
        if (!buttons.Select(button => button.Text).OrderBy(text => text).SequenceEqual(expectedActions.OrderBy(text => text)))
            throw new InvalidOperationException("ProductsForm: la barra de acciones no conserva todos sus botones.");
        foreach (var button in buttons)
        {
            EnsureContained(actions, button, $"acción '{button.Text}'");
            var measured = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (button.ClientSize.Width < measured.Width + 12 || button.ClientSize.Height < measured.Height + 6)
                throw new InvalidOperationException($"ProductsForm: texto cortado en '{button.Text}' a {scale:P0}.");
        }

        if (requestedClientSize.Width <= 980)
        {
            if (filterFields.Select(field => field.Top).Distinct().Count() < 2 ||
                buttons.Select(button => button.Top).Distinct().Count() < 2)
            {
                throw new InvalidOperationException(
                    $"ProductsForm: el viewport estrecho no activó el wrap real de filtros/acciones a {scale:P0}.");
            }
            if (actions.Height <= UITheme.StandardControlHeight + actions.Padding.Vertical)
                throw new InvalidOperationException("ProductsForm: la barra envuelta no aumentó su altura.");
        }

        if (grid.ClientSize.Width <= 0 || grid.ClientSize.Height <= 0 ||
            !grid.ScrollBars.HasFlag(ScrollBars.Horizontal))
        {
            throw new InvalidOperationException("ProductsForm: la grilla no conserva un viewport con scroll horizontal.");
        }

        var category = grid.Columns.Cast<DataGridViewColumn>().SingleOrDefault(column =>
            column.DataPropertyName == "Categoria");
        var subcategory = grid.Columns.Cast<DataGridViewColumn>().SingleOrDefault(column =>
            column.DataPropertyName == "Subcategoria");
        var unassigned = grid.Columns.Cast<DataGridViewColumn>().SingleOrDefault(column =>
            column.DataPropertyName == "SinAsignar");
        if (category is null || !category.Visible || subcategory is null || !subcategory.Visible ||
            unassigned is null || !unassigned.Visible || unassigned.HeaderText != "Sin asignar")
            throw new InvalidOperationException("ProductsForm: Categoría, Subcategoría y Sin asignar deben permanecer visibles.");

        ValidateHeaderContent(grid, scale);
    }

    private static void ValidateHeaderContent(DataGridView grid, float scale)
    {
        foreach (var column in grid.Columns.Cast<DataGridViewColumn>()
                     .Where(column => column.Visible && !string.IsNullOrWhiteSpace(column.HeaderText)))
        {
            var style = column.HeaderCell.InheritedStyle;
            if (style.WrapMode != DataGridViewTriState.False)
            {
                throw new InvalidOperationException(
                    $"ProductsForm: el encabezado '{column.HeaderText}' conserva WrapMode={style.WrapMode} a {scale:P0}.");
            }

            var font = style.Font ?? grid.Font;
            var measured = TextRenderer.MeasureText(
                column.HeaderText,
                font,
                Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            var requiredWidth = measured.Width + style.Padding.Horizontal + 8;
            var requiredHeight = measured.Height + style.Padding.Vertical + 4;
            if (column.Width < requiredWidth || grid.ColumnHeadersHeight < requiredHeight)
            {
                throw new InvalidOperationException(
                    $"ProductsForm: encabezado '{column.HeaderText}' recortado a {scale:P0}; " +
                    $"columna={column.Width}, requeridoAncho={requiredWidth}, " +
                    $"alto={grid.ColumnHeadersHeight}, requeridoAlto={requiredHeight}.");
            }
        }
    }

    private static void SmokeProductWorkspace(
        SmokeFixture fixture,
        float scale,
        string outputDirectory,
        ICollection<string> log)
    {
        using var form = new ProductWorkspaceForm(
            1, null!, null!, null!, null!, null!, null!, null!, canManage: true, loadDataOnShow: false);
        var product = fixture.Product(
            "Producto ficha",
            SmokeFixture.BarroCategoryId,
            SmokeFixture.OllasSubcategoryId,
            "Ollas");
        form.ShowProductForSmoke(new ProductCatalogDetail(product, [new ProductSkuSummary(
            1, product.ProductoId, product.CodigoInterno, null, true, true, null, null, null,
            product.PrecioVentaCentavos, product.PrecioCompraCentavos,
            product.StockExhibicion, product.StockBodega, product.StockSinAsignar,
            product.StockReservado, product.StockActual, product.StockDisponible) ]));
        Capture(form, scale, Path.Combine(outputDirectory, $"product-workspace-subcategory-{ScaleName(scale)}.png"));
        Assert(form.GeneralInfoForSmoke.Text.Contains("Subcategoría\nOllas", StringComparison.Ordinal),
            "Ficha: no muestra la subcategoría junto a la categoría.");
        log.Add($"ProductWorkspace ficha categoría/subcategoría @{scale:P0}=OK");
    }

    private static void Capture(Form form, float scale, string output, Action<Form>? validate = null)
    {
        var requestedClientSize = form.ClientSize;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.ShowInTaskbar = false;
        if (scale != 1f)
        {
            form.Scale(new SizeF(scale, scale));
        }

        var hosted = form is ProductsForm && !form.TopLevel;
        if (hosted)
        {
            // A top-level Form is clamped by the desktop work area. Show the
            // hosted control, then restore the scaled logical viewport.
            form.Show();
            form.ClientSize = new Size(
                (int)Math.Round(requestedClientSize.Width * scale),
                (int)Math.Round(requestedClientSize.Height * scale));
        }
        else
        {
            form.Show();
        }
        form.PerformLayout();
        validate?.Invoke(form);
        ValidateVisibleInputs(form, scale);
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        if (!hosted) form.Hide();
    }

    private static void ValidateVisibleInputs(Form form, float scale)
    {
        foreach (var input in Descendants(form).Where(control =>
                     control.Visible && control is TextBox or ComboBox or NumericUpDown or Button))
        {
            if (input.Width <= 0 || input.Height <= 0 || input.Parent is null)
            {
                throw new InvalidOperationException(
                    $"{form.GetType().Name}: control {input.GetType().Name} fuera del layout {input.Bounds} a {scale:P0}.");
            }
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private static string ScaleName(float scale) => $"{scale * 100:N0}";

    private static string SizeName(Size size) => $"{size.Width}x{size.Height}";

    private static void EnsureContained(Control parent, Control child, string description)
    {
        if (!parent.ClientRectangle.Contains(child.Bounds))
            throw new InvalidOperationException(
                $"{parent.GetType().Name}: {description} fuera de su contenedor {child.Bounds} / {parent.ClientRectangle}.");
    }

    private static void EnsureContained(Form form, Control child, string description)
    {
        var formBounds = BoundsInAncestor(child, form);
        if (!form.ClientRectangle.Contains(formBounds))
        {
            var products = form as ProductsForm;
            var rootBounds = products?.RootForSmoke.Bounds.ToString() ?? "?";
            var tabsBounds = products?.TabsForSmoke.Bounds.ToString() ?? "?";
            throw new InvalidOperationException(
                $"ProductsForm: {description} fuera del área cliente {formBounds} / {form.ClientRectangle}; " +
                $"size={form.Size}, root={rootBounds}, tabs={tabsBounds}, chain={DescribeChain(child, form)}.");
        }
    }

    private static Rectangle BoundsInAncestor(Control child, Control ancestor)
    {
        var location = Point.Empty;
        Control? current = child;
        while (current is not null && current != ancestor)
        {
            location.Offset(current.Left, current.Top);
            current = current.Parent;
        }
        if (current is null)
            throw new InvalidOperationException("No se pudo convertir el control al área del formulario.");
        return new Rectangle(location, child!.Size);
    }

    private static string DescribeChain(Control child, Control ancestor)
    {
        var parts = new List<string>();
        for (var current = child; current is not null; current = current.Parent)
        {
            parts.Add($"{current.GetType().Name}:{current.Bounds}");
            if (current == ancestor) break;
        }
        return string.Join(" <- ", parts);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class SmokeFixture
    {
        public const int BarroCategoryId = 1;
        public const int NoChildrenCategoryId = 2;
        public const int DefaultCategoryId = 3;
        public const int OllasSubcategoryId = 10;
        public const int InactiveSubcategoryId = 11;

        public SmokeFixture()
        {
            Categories = new SmokeCategoryService([
                new CategorySummary(BarroCategoryId, "Barro negro", null, true),
                new CategorySummary(NoChildrenCategoryId, "Madera", null, true),
                new CategorySummary(DefaultCategoryId, "Sin categoria", null, true)
            ]);
            Subcategories = new SmokeSubcategoryService([
                new SubcategorySummary(OllasSubcategoryId, BarroCategoryId, "Ollas", null, true),
                new SubcategorySummary(InactiveSubcategoryId, BarroCategoryId, "Tazas", null, false)
            ]);
        }

        public SmokeCategoryService Categories { get; }
        public SmokeSubcategoryService Subcategories { get; }

        public ProductSummary Product(string name, int categoryId, int? subcategoryId, string? subcategoryName) =>
            new(
                ProductoId: 1,
                CodigoInterno: "PRD-SMOKE-001",
                Nombre: name,
                Descripcion: "Producto para smoke visual",
                CategoriaId: categoryId,
                CategoriaNombre: Categories.All.Single(category => category.CategoriaId == categoryId).Nombre,
                ImagenLocalPath: null,
                PrecioCompraCentavos: 100,
                PrecioVentaCentavos: 250,
                StockActual: 4,
                StockMinimo: 1,
                Estado: EstadoProducto.ACTIVO,
                FechaCreacion: DateTime.UtcNow,
                FechaModificacion: null,
                Modo: ModoProducto.SIMPLE,
                StockDisponible: 4,
                StockExhibicion: 2,
                StockBodega: 2,
                StockSinAsignar: 0,
                StockReservado: 0,
                SubcategoriaId: subcategoryId,
                SubcategoriaNombre: subcategoryName);
    }

    private sealed class SmokeCategoryService(IReadOnlyList<CategorySummary> categories) : ICategoryService
    {
        public IReadOnlyList<CategorySummary> All { get; } = categories;

        public Task<IReadOnlyList<CategorySummary>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(All);

        public Task<IReadOnlyList<CategorySummary>> GetActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CategorySummary>>(All.Where(category => category.Activa).ToList());

        public Task<CategorySummary> GetDefaultCategoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(All.Single(category => category.Nombre == "Sin categoria"));

        public Task<CategorySummary> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual.");

        public Task<CategorySummary> UpdateAsync(UpdateCategoryRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual.");

        public Task SetActiveAsync(int categoriaId, bool activa, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual.");

        public Task DeleteAsync(int categoriaId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual.");
    }

    private sealed class SmokeSubcategoryService(IReadOnlyList<SubcategorySummary> items) : ISubcategoryService
    {
        public Task<IReadOnlyList<SubcategorySummary>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(items);

        public Task<IReadOnlyList<SubcategorySummary>> GetActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubcategorySummary>>(items.Where(item => item.Activa).ToList());

        public Task<IReadOnlyList<SubcategorySummary>> GetByCategoryAsync(
            int categoriaId,
            bool soloActivas = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubcategorySummary>>(items
                .Where(item => item.CategoriaId == categoriaId && (!soloActivas || item.Activa))
                .OrderBy(item => item.Nombre)
                .ToList());

        public Task<SubcategorySummary?> GetByIdAsync(int subcategoriaId, CancellationToken cancellationToken = default) =>
            Task.FromResult(items.FirstOrDefault(item => item.SubcategoriaId == subcategoriaId));

        public Task<SubcategorySummary> CreateAsync(CreateSubcategoryRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual.");

        public Task<SubcategorySummary> UpdateAsync(UpdateSubcategoryRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual.");

        public Task SetActiveAsync(int subcategoriaId, bool activa, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual.");

        public Task DeleteAsync(int subcategoriaId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual.");
    }

    private sealed class SmokeProductService : IProductService
    {
        public CreateProductRequest? LastCreate { get; private set; }
        public UpdateProductRequest? LastUpdate { get; private set; }

        public Task<IReadOnlyList<ProductSummary>> GetAllAsync(ProductFilter? filter = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductSummary>>([]);

        public Task<IReadOnlyList<ProductSummary>> GetAvailableForSaleAsync(ProductFilter? filter = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductSummary>>([]);

        public Task<IReadOnlyList<ProductSummary>> SearchByNameAsync(string nombre, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductSummary>>([]);

        public Task<ProductSummary?> GetByCodigoInternoAsync(string codigoInterno, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProductSummary?>(null);

        public Task<IReadOnlyList<ProductSummary>> GetLowStockAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductSummary>>([]);

        public Task<ProductSummary> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default)
        {
            LastCreate = request;
            return Task.FromResult(new ProductSummary(
                1,
                request.CodigoInterno ?? "PRD-SMOKE-001",
                request.Nombre,
                request.Descripcion,
                request.CategoriaId ?? SmokeFixture.DefaultCategoryId,
                "Barro negro",
                null,
                request.PrecioCompraCentavos ?? 0,
                request.PrecioVentaCentavos ?? 0,
                request.StockActual ?? 0,
                request.StockMinimo,
                EstadoProducto.ACTIVO,
                DateTime.UtcNow,
                null,
                SubcategoriaId: request.SubcategoriaId));
        }

        public Task<ProductSummary> UpdateAsync(UpdateProductRequest request, CancellationToken cancellationToken = default)
        {
            LastUpdate = request;
            return Task.FromResult(new ProductSummary(
                request.ProductoId,
                request.CodigoInterno,
                request.Nombre,
                request.Descripcion,
                request.CategoriaId,
                "Barro negro",
                null,
                request.PrecioCompraCentavos,
                request.PrecioVentaCentavos,
                0,
                request.StockMinimo,
                EstadoProducto.ACTIVO,
                DateTime.UtcNow,
                null,
                SubcategoriaId: request.SubcategoriaId));
        }

        public Task SetEstadoAsync(int productoId, EstadoProducto estado, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual.");

        public Task DeleteAsync(int productoId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual.");
    }

    private sealed class SmokeProductAssetService : IProductAssetService
    {
        public Task<IReadOnlyList<ProductAssetSummary>> GetByProductAsync(
            int productoId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductAssetSummary>>([]);

        public Task<ProductAssetSummary> ImportAsync(
            ImportProductAssetRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual.");

        public Task SetPrimaryAsync(
            ProductAssetOwnerKind ownerKind,
            int assetId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual.");

        public Task DeleteAsync(
            ProductAssetOwnerKind ownerKind,
            int assetId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual.");

        public Task<string?> GetThumbnailPathAsync(
            ProductAssetOwnerKind ownerKind,
            int assetId,
            int maximumPixels = 400,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<AssetCleanupResult> CleanupOrphansAsync(
            TimeSpan minimumAge,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smoke visual.");
    }

    private sealed class SmokeInstallationContext : IInstallationContext
    {
        public InstallationStatus Status { get; } = new(
            StationConfigurationState.Ready,
            InstallationMode.Principal,
            true,
            Guid.NewGuid(),
            1,
            "smoke",
            null);

        public bool AllowsOperationalWrites => true;
        public void EnsureInitialized() { }
        public void EnsureOperationalWriteAllowed(string operationName) { }
    }
}
