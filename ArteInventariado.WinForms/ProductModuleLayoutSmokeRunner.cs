using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

internal static class ProductModuleLayoutSmokeRunner
{
    public static int Run(string? outputDirectory)
    {
        outputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(Path.GetTempPath(), "ArteInventariado.ProductModuleSmoke")
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var log = new List<string> { $"START={DateTimeOffset.UtcNow:O}" };
        try
        {
            if (Environment.GetEnvironmentVariable("ARTE_PRODUCT_ACTIVATION_SMOKE_ONLY") == "1")
            {
                Progress("stage=ValidateProductsActivationRedraw");
                ValidateProductsActivationRedraw(outputDirectory, log);
                log.Add("RESULT=OK");
                return 0;
            }

            if (Environment.GetEnvironmentVariable("ARTE_PRODUCT_IMAGE_SMOKE_ONLY") == "1")
            {
                Progress("stage=ValidateProductImageBatchMatching");
                ValidateProductImageBatchMatching(log);
                foreach (var scale in new[] { 1f, 1.25f, 1.5f })
                {
                    Progress($"stage=ProductEditImage-start;scale={ScaleName(scale)}");
                    SmokeProductImageEditor(scale, outputDirectory, log);
                    Progress($"stage=ProductImageBatch-start;scale={ScaleName(scale)}");
                    SmokeProductImageBatch(scale, outputDirectory, log);
                }
                log.Add("RESULT=OK");
                return 0;
            }

            Progress("stage=ValidateCategoryAndSubcategoryWindowSizes");
            SubcategoryLayoutSmokeRunner.ValidateWindowSizesForSmoke(
                Path.Combine(outputDirectory, "category-subcategory"), log);
            Progress("stage=ValidateInventoryDialogRules");
            ValidateInventoryDialogRules();
            Progress("stage=ValidateCatalogFiltersAndLocationSelection");
            ValidateCatalogFiltersAndLocationSelection(log);
            ValidateEmployeeInventoryFilters(log);
            ValidateProductImageBatchMatching(log);
            foreach (var scale in new[] { 1f, 1.25f, 1.5f })
            {
                Progress($"stage=scale-start;scale={ScaleName(scale)}");
                var installation = new PrincipalInstallationContext();
                var products = new ProductsForm(null!, null!, null!, null!, null!, null!, null!, null!, installation, loadDataOnShow: false)
                { WindowState = FormWindowState.Normal, ClientSize = new Size(1280, 720) };
                products.MinimumSize = Size.Empty;
                products.MaximumSize = Size.Empty;
                products.TopLevel = false;
                products.FormBorderStyle = FormBorderStyle.None;
                products.ClientSize = new Size(1280, 720);
                Progress($"stage=ProductsForm-created;scale={ScaleName(scale)}");
                Smoke(products, scale, Path.Combine(outputDirectory, $"products-{ScaleName(scale)}.png"), log);
                Progress($"stage=ProductsForm-complete;scale={ScaleName(scale)}");

                var workspace = new ProductWorkspaceForm(1, null!, null!, null!, null!, null!, null!, null!, canManage: true, loadDataOnShow: false);
                Progress($"stage=ProductWorkspaceForm-created;scale={ScaleName(scale)}");
                SmokeWorkspace(workspace, scale, outputDirectory, log);
                Progress($"stage=ProductWorkspaceForm-complete;scale={ScaleName(scale)}");

                var editor = new ProductEditForm(null!, null!, loadDataOnShow: false);
                Progress($"stage=ProductEditForm-created;scale={ScaleName(scale)}");
                Smoke(editor, scale, Path.Combine(outputDirectory, $"product-editor-{ScaleName(scale)}.png"), log);
                Progress($"stage=ProductEditForm-complete;scale={ScaleName(scale)}");
                var imageEditor = new ProductEditForm(null!, null!, loadDataOnShow: false,
                    assetService: new LayoutSmokeProductAssetService(), canManageImages: true)
                { WindowState = FormWindowState.Normal, MinimumSize = Size.Empty };
                var previewPath = Path.Combine(outputDirectory, "product-image-preview-smoke.png");
                using (var sample = new Bitmap(48, 36))
                {
                    using var graphics = Graphics.FromImage(sample);
                    graphics.Clear(UITheme.PrimaryColor);
                    sample.Save(previewPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                var selectImage = imageEditor.SelectImageForSmokeAsync(previewPath);
                var previewTimeout = System.Diagnostics.Stopwatch.StartNew();
                while (!selectImage.IsCompleted && previewTimeout.Elapsed < TimeSpan.FromSeconds(10))
                {
                    System.Windows.Forms.Application.DoEvents();
                    Thread.Sleep(1);
                }
                Assert(selectImage.IsCompleted,
                    "ProductEditForm: expiró la espera de la vista previa de imagen.");
                selectImage.GetAwaiter().GetResult();
                Assert(imageEditor.PendingImagePathForSmoke == previewPath && imageEditor.ImagePreviewForSmoke.Image is not null,
                    "ProductEditForm: no se mostró la vista previa del archivo seleccionado.");
                Smoke(imageEditor, scale, Path.Combine(outputDirectory, $"product-editor-image-{ScaleName(scale)}.png"), log);
                Progress($"stage=ProductEditForm-image-complete;scale={ScaleName(scale)}");
                var batchImages = new ProductImageBatchAssignmentForm([], null!, null!)
                { WindowState = FormWindowState.Normal, ClientSize = new Size(1220, 760), MinimumSize = Size.Empty };
                Smoke(batchImages, scale, Path.Combine(outputDirectory, $"product-images-batch-{ScaleName(scale)}.png"), log);
                Progress($"stage=ProductImageBatchAssignmentForm-complete;scale={ScaleName(scale)}");
                Progress($"stage=GenerateMissingBarcodesForm-start;scale={ScaleName(scale)}");
                Smoke(new GenerateMissingBarcodesForm(new LayoutSmokeBarcodeService(), []), scale,
                    Path.Combine(outputDirectory, $"barcode-generate-{ScaleName(scale)}.png"), log);
                Progress($"stage=GenerateMissingBarcodesForm-complete;scale={ScaleName(scale)}");
                Progress($"stage=LabelBatchSelectionForm-start;scale={ScaleName(scale)}");
                Smoke(new LabelBatchSelectionForm([]), scale,
                    Path.Combine(outputDirectory, $"label-batch-{ScaleName(scale)}.png"), log);
                Progress($"stage=LabelBatchSelectionForm-complete;scale={ScaleName(scale)}");
                Progress($"stage=LabelQuantityDialog-start;scale={ScaleName(scale)}");
                Smoke(new LabelQuantityDialog(), scale,
                    Path.Combine(outputDirectory, $"label-quantity-{ScaleName(scale)}.png"), log);
                Progress($"stage=LabelQuantityDialog-complete;scale={ScaleName(scale)}");

                var sku = BuildInventorySku();
                var available = new Dictionary<ArteInventariado.Domain.Enums.UbicacionInventario, int>
                {
                    [ArteInventariado.Domain.Enums.UbicacionInventario.EXHIBICION] = 1,
                    [ArteInventariado.Domain.Enums.UbicacionInventario.BODEGA] = 21,
                    [ArteInventariado.Domain.Enums.UbicacionInventario.SIN_ASIGNAR] = 30
                };
                Progress($"stage=InventoryTransferDialog-start;scale={ScaleName(scale)}");
                Smoke(new InventoryTransferDialog(sku, classify: false, available), scale,
                    Path.Combine(outputDirectory, $"inventory-transfer-{ScaleName(scale)}.png"), log);
                Smoke(new InventoryTransferDialog(sku, classify: true, available), scale,
                    Path.Combine(outputDirectory, $"inventory-classify-{ScaleName(scale)}.png"), log);
                Progress($"stage=InventoryTransferDialog-complete;scale={ScaleName(scale)}");
                Smoke(new InventoryWasteDialog(sku, available), scale,
                    Path.Combine(outputDirectory, $"inventory-waste-{ScaleName(scale)}.png"), log);
                Progress($"stage=InventoryWasteDialog-complete;scale={ScaleName(scale)}");
                Smoke(new ExhibitionMinimumDialog(sku), scale,
                    Path.Combine(outputDirectory, $"inventory-minimum-{ScaleName(scale)}.png"), log);
                var adjustmentBalances = new[]
                {
                    new InventoryBalanceSummary(sku.ProductoSkuId, sku.ProductoId, sku.CodigoSku,
                        "Producto smoke", ArteInventariado.Domain.Enums.UbicacionInventario.EXHIBICION, 1, 0, 1, 1),
                    new InventoryBalanceSummary(sku.ProductoSkuId, sku.ProductoId, sku.CodigoSku,
                        "Producto smoke", ArteInventariado.Domain.Enums.UbicacionInventario.BODEGA, 21, 0, 21, 1),
                    new InventoryBalanceSummary(sku.ProductoSkuId, sku.ProductoId, sku.CodigoSku,
                        "Producto smoke", ArteInventariado.Domain.Enums.UbicacionInventario.SIN_ASIGNAR, 30, 0, 30, 1)
                };
                Smoke(new StockAdjustmentForm(null!, sku, adjustmentBalances), scale,
                    Path.Combine(outputDirectory, $"inventory-adjustment-{ScaleName(scale)}.png"), log);
                Smoke(new ExpenseCaptureForm([], "Gasto de prueba"), scale,
                    Path.Combine(outputDirectory, $"cash-expense-{ScaleName(scale)}.png"), log);
                var employee = new EmployeeActivitySummary(
                    1, "empleado-smoke", "Empleado de prueba", RolUsuario.EMPLEADO,
                    true, DateTime.UtcNow, null, 30);
                Smoke(new EmployeePaymentForm(employee, new LayoutSmokeEmployeePaymentService()), scale,
                    Path.Combine(outputDirectory, $"employee-payment-{ScaleName(scale)}.png"), log);
                var employeeInventory = new EmployeeInventoryLookupForm(null!, null!, loadDataOnShow: false)
                {
                    ClientSize = new Size(1180, 700)
                };
                employeeInventory.BindItemsForSmoke(
                [
                    EmployeeItem("SKU-A", "Alfa", 1, 11, "Tallado", DateTime.UtcNow, 5, 2, 2, 3, 0, false),
                    EmployeeItem("SKU-B", "Bravo", 2, null, null, DateTime.UtcNow.AddDays(-1), 4, 1, 0, 0, 4, false),
                    EmployeeItem("SKU-C", "Charlie", 1, null, null, DateTime.UtcNow.AddDays(-2), 8, 2, 1, 7, 0, false)
                ]);
                Smoke(employeeInventory, scale,
                    Path.Combine(outputDirectory, $"employee-inventory-{ScaleName(scale)}.png"), log);
                Progress($"stage=scale-complete;scale={ScaleName(scale)}");
            }
            Progress("stage=ValidateProductsDisposeIsIdempotent");
            ValidateProductsDisposeIsIdempotent(log);
            Progress("stage=complete");
            log.Add("RESULT=OK");
            return 0;
        }
        catch (Exception ex)
        {
            log.Add("RESULT=FAILED"); log.Add(ex.ToString()); return 1;
        }
        finally
        {
            log.Add($"END={DateTimeOffset.UtcNow:O}");
            File.WriteAllLines(Path.Combine(outputDirectory, "product-module-smoke.log"), log);
        }
    }

    private static void ValidateProductImageBatchMatching(ICollection<string> log)
    {
        var products = new[]
        {
            new ProductSummary(41, "ART-0041", "Cartera mediana", null, 1, "Bolsos", null,
                0, 1000, 0, 0, EstadoProducto.ACTIVO, DateTime.UtcNow, null,
                ManagedImageRelativePath: "Skus/sku-rosa.png", TieneImagen: true),
            new ProductSummary(42, "ART-0042", "Cartera grande", null, 1, "Bolsos", null,
                0, 1200, 0, 0, EstadoProducto.ACTIVO, DateTime.UtcNow, null)
        };
        using var form = new ProductImageBatchAssignmentForm(products, null!, null!);
        form.ImageFilterForSmoke.SelectedIndex = 1;
        Assert(form.VisibleProductCodesForSmoke.SequenceEqual(["ART-0041"]),
            "Asignación en lote: el filtro Con imagen no incluyó un producto con imagen de SKU.");
        form.ImageFilterForSmoke.SelectedIndex = 2;
        Assert(form.VisibleProductCodesForSmoke.SequenceEqual(["ART-0042"]),
            "Asignación en lote: el filtro Sin imagen no excluyó los productos con imagen.");
        form.ImageFilterForSmoke.SelectedIndex = 0;
        form.SearchBoxForSmoke.Text = "GRANDE";
        Assert(form.VisibleProductCodesForSmoke.SequenceEqual(["ART-0042"]),
            "Asignación en lote: la búsqueda por nombre no filtró el catálogo.");
        form.SearchBoxForSmoke.Clear();

        form.SelectProductForSmoke(41, selected: true);
        form.SelectProductForSmoke(42, selected: true);
        form.SelectionFilterForSmoke.SelectedIndex = 1;
        Assert(form.VisibleProductCodesForSmoke.SequenceEqual(["ART-0041", "ART-0042"]),
            "Asignación en lote: Solo seleccionados no mostró únicamente los productos marcados.");
        form.SelectProductForSmoke(41, selected: false);
        Assert(form.VisibleProductCodesForSmoke.SequenceEqual(["ART-0042"]) &&
               form.SelectedProductCodesForSmoke.SequenceEqual(["ART-0042"]),
            "Asignación en lote: desmarcar una fila no quitó solamente ese producto de la selección visible.");
        form.ClearProductSelectionForSmoke();
        Assert(form.SelectionFilterForSmoke.SelectedIndex == 0 && form.VisibleProductCodesForSmoke.Count == 2 &&
               form.SelectedProductCodesForSmoke.Count == 0,
            "Asignación en lote: limpiar selección no regresó a mostrar todos los productos.");

        form.SelectAllProductsForSmoke();
        form.AddSkuTargetForSmoke(41, 4101, "SKU-ROSA");
        form.LoadFilesForSmoke([
            Path.Combine("imagenes", "ART-0041.png"),
            Path.Combine("imagenes", "SKU-ROSA.jpg"),
            Path.Combine("imagenes", "sin-codigo.png")
        ]);
        var matched = form.FileTargetsForSmoke;
        Assert(matched[0].TargetKey == "P:41" && matched[1].TargetKey == "S:4101" && matched[2].TargetKey.Length == 0,
            "Asignación en lote: el nombre exacto no resolvió código de producto/SKU o asignó un archivo sin coincidencia.");

        form.LoadFilesForSmoke([
            Path.Combine("imagenes", "ART-0041.png"),
            Path.Combine("imagenes", "ART-0041.jpg")
        ]);
        Assert(form.FileTargetsForSmoke.All(row => row.TargetKey.Length == 0),
            "Asignación en lote: dos archivos con el mismo código debieron quedar para revisión manual.");
        log.Add("Asignación en lote: coincidencia exacta por código/SKU y duplicados ambiguos sin autoasignación=OK");

        ValidateProductImageReplacementSafety(log);
    }

    private static void ValidateProductImageReplacementSafety(ICollection<string> log)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ArteInventariado.ImageBatch.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var imagePath = Path.Combine(directory, "ART-0041.png");
        File.WriteAllBytes(imagePath, [1, 2, 3]);
        try
        {
            var product = new ProductSummary(41, "ART-0041", "Cartera mediana", null, 1, "Bolsos", null,
                0, 1000, 0, 0, EstadoProducto.ACTIVO, DateTime.UtcNow, null,
                ManagedImageRelativePath: "Products/old.png", TieneImagen: true);

            var keepService = new BatchSmokeProductAssetService();
            var keptPrimary = keepService.SeedPrimary(41, "old.png");
            using (var keepForm = new ProductImageBatchAssignmentForm([product], null!, keepService))
            {
                keepForm.SelectAllProductsForSmoke();
                keepForm.LoadFilesForSmoke([imagePath]);
                keepForm.ReviewImageRowForSmokeAsync(0).GetAwaiter().GetResult();
                Assert(keepForm.PreviousPrimaryAssetIdForSmoke(0) == keptPrimary.AssetId,
                    "Asignación en lote: no mostró la imagen principal actual para decidir conservarla.");
                keepForm.SetImageDecisionForSmoke(0, makePrimary: true, deletePrevious: false);
                keepForm.ProcessImageRowForSmokeAsync(0).GetAwaiter().GetResult();
                var assets = keepService.ForProduct(41);
                Assert(assets.Count == 2 && assets.Any(asset => asset.AssetId == keptPrimary.AssetId) &&
                       assets.Single(asset => asset.AssetId != keptPrimary.AssetId).IsPrimary && keepService.DeleteCount == 0,
                    "Asignación en lote: conservar la anterior debió mantenerla y guardar la nueva como principal.");
            }

            var replaceService = new BatchSmokeProductAssetService();
            var replacedPrimary = replaceService.SeedPrimary(41, "old.png");
            using (var replaceForm = new ProductImageBatchAssignmentForm([product], null!, replaceService))
            {
                replaceForm.SelectAllProductsForSmoke();
                replaceForm.LoadFilesForSmoke([imagePath]);
                replaceForm.ReviewImageRowForSmokeAsync(0).GetAwaiter().GetResult();
                replaceForm.SetImageDecisionForSmoke(0, makePrimary: true, deletePrevious: true);
                replaceForm.ProcessImageRowForSmokeAsync(0).GetAwaiter().GetResult();
                var assets = replaceService.ForProduct(41);
                Assert(assets.Count == 1 && assets[0].AssetId != replacedPrimary.AssetId && assets[0].IsPrimary &&
                       replaceService.DeleteCount == 1,
                    "Asignación en lote: solo debía eliminar la anterior después de confirmar la nueva como principal.");
            }

            var failedService = new BatchSmokeProductAssetService { FailImports = true };
            var preservedPrimary = failedService.SeedPrimary(41, "old.png");
            using (var failedForm = new ProductImageBatchAssignmentForm([product], null!, failedService))
            {
                failedForm.SelectAllProductsForSmoke();
                failedForm.LoadFilesForSmoke([imagePath]);
                failedForm.ReviewImageRowForSmokeAsync(0).GetAwaiter().GetResult();
                failedForm.SetImageDecisionForSmoke(0, makePrimary: true, deletePrevious: true);
                failedForm.ProcessImageRowForSmokeAsync(0).GetAwaiter().GetResult();
                Assert(failedService.ForProduct(41).Single().AssetId == preservedPrimary.AssetId &&
                       failedService.DeleteCount == 0,
                    "Asignación en lote: la imagen anterior debe conservarse si falla la importación nueva.");
            }

            log.Add("Asignación en lote: conserva por defecto, reemplazo explícito y fallo seguro sin borrar anterior=OK");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void SmokeProductImageEditor(float scale, string outputDirectory, ICollection<string> log)
    {
        using var sample = new Bitmap(48, 36);
        using (var graphics = Graphics.FromImage(sample))
        {
            graphics.Clear(UITheme.PrimaryColor);
        }
        var previewPath = Path.Combine(outputDirectory, "product-image-preview-smoke.png");
        sample.Save(previewPath, System.Drawing.Imaging.ImageFormat.Png);
        var editor = new ProductEditForm(null!, null!, loadDataOnShow: false,
            assetService: new LayoutSmokeProductAssetService(), canManageImages: true)
        { WindowState = FormWindowState.Normal, MinimumSize = Size.Empty };
        var selectPreview = editor.SelectImageForSmokeAsync(previewPath);
        while (!selectPreview.IsCompleted)
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(1);
        }
        selectPreview.GetAwaiter().GetResult();
        Assert(editor.PendingImagePathForSmoke == previewPath && editor.ImagePreviewForSmoke.Image is not null,
            "ProductEditForm: no se mostró la vista previa del archivo seleccionado.");
        Smoke(editor, scale, Path.Combine(outputDirectory, $"product-editor-image-{ScaleName(scale)}.png"), log);
    }

    private static void SmokeProductImageBatch(float scale, string outputDirectory, ICollection<string> log)
    {
        var currentPath = Path.Combine(outputDirectory, "image-batch-current-preview.png");
        var newPath = Path.Combine(outputDirectory, "IMAGE-BATCH-SMOKE.png");
        SaveSampleImage(currentPath, Color.FromArgb(235, 126, 175));
        SaveSampleImage(newPath, Color.FromArgb(87, 159, 126));
        var product = new ProductSummary(9001, "IMAGE-BATCH-SMOKE", "Vista previa de reemplazo", null,
            1, "Pruebas", null, 0, 1000, 0, 0, EstadoProducto.ACTIVO, DateTime.UtcNow, null,
            ManagedImageRelativePath: "Products/100.png", TieneImagen: true);
        var assets = new BatchSmokeProductAssetService { ThumbnailPath = currentPath };
        assets.SeedPrimary(product.ProductoId, "imagen-actual.png");
        var batch = new ProductImageBatchAssignmentForm([product], null!, assets)
        { WindowState = FormWindowState.Normal, ClientSize = new Size(1220, 760), MinimumSize = Size.Empty };
        batch.SelectAllProductsForSmoke();
        batch.LoadFilesForSmoke([newPath]);
        Assert(batch.FileTargetsForSmoke.Single().TargetKey == "P:9001",
            "ProductImageBatchAssignmentForm: no asignó la coincidencia exacta por código interno.");
        Smoke(batch, scale, Path.Combine(outputDirectory, $"product-images-batch-{ScaleName(scale)}.png"), log);
    }

    private static void SaveSampleImage(string path, Color color)
    {
        using var bitmap = new Bitmap(420, 280);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var highlight = new SolidBrush(Color.FromArgb(180, Color.White)))
        {
            graphics.Clear(color);
            graphics.FillEllipse(highlight, new Rectangle(110, 55, 200, 170));
        }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static void ValidateProductsActivationRedraw(string outputDirectory, ICollection<string> log)
    {
        using var form = new ProductsForm(
            null!, null!, null!, null!, null!, null!, null!, null!,
            new PrincipalInstallationContext(),
            loadDataOnShow: false)
        {
            WindowState = FormWindowState.Normal,
            ClientSize = new Size(1280, 800),
            MinimumSize = Size.Empty,
            MaximumSize = Size.Empty
        };
        form.BindProductsForSmoke(
        [
            Product(
                501,
                "VIS-501",
                "Producto visual de prueba",
                1,
                null,
                null,
                new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc),
                5,
                0,
                2,
                3,
                0,
                false)
        ]);
        form.ModeFilterForSmoke.SelectedIndex = 1;

        var activations = 0;
        form.Activated += (_, _) => activations++;
        form.Show();
        PumpMessages();

        foreach (var tabIndex in new[] { 0, 1, 2 })
        {
            form.TabsForSmoke.SelectedIndex = tabIndex;
            PumpMessages();
            var expectedGrid = tabIndex switch
            {
                1 => form.ExhibitionGridForSmoke,
                2 => form.WarehouseGridForSmoke,
                _ => form.GeneralGridForSmoke
            };
            var expectedRows = expectedGrid.Rows.Count;
            var activationsBeforeRestore = activations;

            form.WindowState = FormWindowState.Minimized;
            PumpMessages();
            form.WindowState = FormWindowState.Normal;
            form.Activate();
            PumpMessages();

            Assert(form.Visible, $"ProductsForm no volvió visible después de restaurar la pestaña {tabIndex}.");
            Assert(form.TabsForSmoke.SelectedIndex == tabIndex,
                $"ProductsForm cambió la pestaña activa después de restaurar la pestaña {tabIndex}.");
            Assert(expectedGrid.Rows.Count == expectedRows,
                $"ProductsForm perdió filas después de restaurar la pestaña {tabIndex}.");
            Assert(expectedGrid.IsHandleCreated && expectedGrid.Visible,
                $"ProductsForm no dejó visible la grilla de la pestaña {tabIndex} después de restaurar.");
            AssertIds([501], form.VisibleProductIdsForSmoke,
                $"productos conservados tras restaurar la pestaña {tabIndex}");
            Assert(form.ModeFilterForSmoke.SelectedIndex == 1,
                $"ProductsForm perdió el filtro activo después de restaurar la pestaña {tabIndex}.");
            Assert(activations > activationsBeforeRestore,
                $"ProductsForm no recibió Activated tras restaurar la pestaña {tabIndex} (activaciones={activations}).");
            ValidateProductsResponsiveLayout(form, 1f, validateClearButton: false);
        }

        SaveScreenshot(form, Path.Combine(outputDirectory, "products-activation-redraw.png"));
        log.Add("ProductsForm: minimiza/restaura las tres pestañas; activación, layout, filtros y productos conservados=OK (datos ficticios, sin DB)");
    }

    private static void PumpMessages()
    {
        for (var iteration = 0; iteration < 8; iteration++)
        {
            System.Windows.Forms.Application.DoEvents();
        }
    }

    private static void ValidateCatalogFiltersAndLocationSelection(ICollection<string> log)
    {
        using var form = new ProductsForm(
            null!, null!, null!, null!, null!, null!, null!, null!,
            new PrincipalInstallationContext(),
            loadDataOnShow: false);
        form.BindFilterOptionsForSmoke(
            [new CategorySummary(1, "Madera", null, true), new CategorySummary(2, "Barro", null, true)],
            [new SubcategorySummary(11, 1, "Tallado", null, true)]);

        var createdAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        form.BindProductsForSmoke(
        [
            Product(101, "ART-101", "Alfa", 1, 11, "Tallado", createdAt, 5, 2, 2, 3, 0, true),
            Product(102, "ART-102", "Bravo", 2, null, null, createdAt.AddDays(2), 4, 1, 0, 0, 4, false),
            Product(103, "ART-103", "Charlie", 1, null, null, createdAt.AddDays(1), 8, 2, 1, 7, 0, false),
            Product(104, "ART-104", "Delta", 2, null, null, createdAt.AddDays(3), 0, 1, 0, 0, 0, false)
        ]);

        AssertIds([101, 102, 103, 104], form.VisibleProductIdsForSmoke, "filtro inicial");
        form.CategoryFilterForSmoke.SelectedValue = 1;
        AssertIds([101, 103], form.VisibleProductIdsForSmoke, "categoría");
        form.SubcategoryFilterForSmoke.SelectedValue = 11;
        AssertIds([101], form.VisibleProductIdsForSmoke, "subcategoría");

        form.CategoryFilterForSmoke.SelectedValue = 0;
        form.SubcategoryFilterForSmoke.SelectedValue = -1;
        AssertIds([102, 103, 104], form.VisibleProductIdsForSmoke, "sin subcategoría");
        form.SubcategoryFilterForSmoke.SelectedValue = 0;
        form.ImageFilterForSmoke.SelectedValue = 1;
        AssertIds([101], form.VisibleProductIdsForSmoke, "con imagen");
        form.ImageFilterForSmoke.SelectedValue = 2;
        AssertIds([102, 103, 104], form.VisibleProductIdsForSmoke, "sin imagen");
        form.ImageFilterForSmoke.SelectedValue = 0;
        form.LocationFilterForSmoke.SelectedValue = 2;
        AssertIds([101, 103], form.VisibleProductIdsForSmoke, "bodega");
        form.LocationFilterForSmoke.SelectedValue = 3;
        AssertIds([102], form.VisibleProductIdsForSmoke, "sin asignar");
        form.LocationFilterForSmoke.SelectedValue = 0;
        form.AvailabilityFilterForSmoke.SelectedValue = 2;
        AssertIds([104], form.VisibleProductIdsForSmoke, "sin existencias");
        form.AvailabilityFilterForSmoke.SelectedValue = 0;
        form.SortFilterForSmoke.SelectedValue = 2;
        AssertIds([104, 102, 103, 101], form.VisibleProductIdsForSmoke, "alta más reciente");

        form.StateFilterForSmoke.SelectedIndex = 1;
        form.ModeFilterForSmoke.SelectedIndex = 2;
        form.BarcodeFilterForSmoke.SelectedIndex = 3;
        form.LowStockFilterForSmoke.Checked = true;
        form.CategoryFilterForSmoke.SelectedValue = 1;
        form.SubcategoryFilterForSmoke.SelectedValue = 11;
        form.ImageFilterForSmoke.SelectedValue = 2;
        form.LocationFilterForSmoke.SelectedValue = 3;
        form.AvailabilityFilterForSmoke.SelectedValue = 2;
        form.SortFilterForSmoke.SelectedValue = 3;
        Assert(form.ShouldShowClearButtonForSmoke,
            "La X no aparece con filtros activos y búsqueda vacía en ADMIN.");
        form.SetSearchTextForLayoutSmoke("ART-101");
        Assert(form.ShouldShowClearButtonForSmoke,
            "La X no aparece con búsqueda y filtros activos.");
        form.ClearSearchForSmoke();
        form.CancelPendingSearchForSmoke();
        Assert(string.IsNullOrEmpty(form.SearchBoxForSmoke.Text), "La X no limpió la búsqueda del catálogo.");
        Assert(form.StateFilterForSmoke.SelectedIndex == 0 &&
               form.ModeFilterForSmoke.SelectedIndex == 0 &&
               form.BarcodeFilterForSmoke.SelectedIndex == 0 &&
               !form.LowStockFilterForSmoke.Checked &&
               (int)form.CategoryFilterForSmoke.SelectedValue == 0 &&
               (int)form.SubcategoryFilterForSmoke.SelectedValue == 0 &&
               (int)form.ImageFilterForSmoke.SelectedValue == 0 &&
               (int)form.LocationFilterForSmoke.SelectedValue == 0 &&
               (int)form.AvailabilityFilterForSmoke.SelectedValue == 0 &&
               (int)form.SortFilterForSmoke.SelectedValue == 0,
            "La X no devolvió todos los filtros de ADMIN a sus valores predeterminados.");
        AssertIds([101, 102, 103, 104], form.VisibleProductIdsForSmoke, "reiniciar catálogo");

        form.SortFilterForSmoke.SelectedValue = 0;
        form.TabsForSmoke.SelectedIndex = 1;
        form.ExhibitionGridForSmoke.CurrentCell = form.ExhibitionGridForSmoke.Rows[1].Cells[0];
        Assert(
            form.SelectedProductForSmoke?.ProductoId == 103,
            "Abrir ficha desde Exhibición no respetó la fila activa.");
        form.TabsForSmoke.SelectedIndex = 2;
        form.WarehouseGridForSmoke.CurrentCell = form.WarehouseGridForSmoke.Rows[1].Cells[0];
        Assert(
            form.SelectedProductForSmoke?.ProductoId == 103,
            "Abrir ficha desde Bodega no respetó la fila activa.");

        log.Add("Productos e inventario: filtros, orden, fila activa y X para restablecer búsqueda/filtros=OK");
    }

    private static ProductSummary Product(
        int id,
        string code,
        string name,
        int categoryId,
        int? subcategoryId,
        string? subcategoryName,
        DateTime createdAt,
        int stock,
        int minimum,
        int exhibition,
        int warehouse,
        int unassigned,
        bool hasImage) => new(
            id,
            code,
            name,
            null,
            categoryId,
            categoryId == 1 ? "Madera" : "Barro",
            null,
            100,
            200,
            stock,
            minimum,
            EstadoProducto.ACTIVO,
            createdAt,
            null,
            ModoProducto.SIMPLE,
            stock,
            exhibition,
            warehouse,
            unassigned,
            0,
            hasImage ? "assets/preview.png" : null,
            EstadoBarcodeProducto.SinBarcode,
            true,
            subcategoryId,
            subcategoryName,
            hasImage);

    private static void ValidateEmployeeInventoryFilters(ICollection<string> log)
    {
        using var form = new EmployeeInventoryLookupForm(null!, null!, loadDataOnShow: false);
        var createdAt = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);
        form.BindItemsForSmoke(
        [
            EmployeeItem("SKU-A", "Alfa", 1, 11, "Tallado", createdAt, 5, 2, 2, 3, 0, true),
            EmployeeItem("SKU-B", "Bravo", 2, null, null, createdAt.AddDays(2), 4, 1, 0, 0, 4, false),
            EmployeeItem("SKU-C", "Charlie", 1, null, null, createdAt.AddDays(1), 8, 2, 1, 7, 0, false),
            EmployeeItem("SKU-D", "Delta", 2, null, null, createdAt.AddDays(3), 0, 1, 0, 0, 0, false)
        ]);

        AssertEmployeeSkus(["SKU-A", "SKU-B", "SKU-C", "SKU-D"], form, "filtro inicial de empleado");
        form.CategoryFilterForSmoke.SelectedValue = 1;
        AssertEmployeeSkus(["SKU-A", "SKU-C"], form, "categoría de empleado");
        form.SubcategoryFilterForSmoke.SelectedValue = 11;
        AssertEmployeeSkus(["SKU-A"], form, "subcategoría de empleado");

        form.CategoryFilterForSmoke.SelectedValue = 0;
        form.SubcategoryFilterForSmoke.SelectedValue = -1;
        AssertEmployeeSkus(["SKU-B", "SKU-C", "SKU-D"], form, "sin subcategoría de empleado");
        form.SubcategoryFilterForSmoke.SelectedValue = 0;
        form.ImageFilterForSmoke.SelectedValue = 2;
        AssertEmployeeSkus(["SKU-B", "SKU-C", "SKU-D"], form, "sin imagen de empleado");
        form.ImageFilterForSmoke.SelectedValue = 0;
        form.LocationFilterForSmoke.SelectedValue = 2;
        AssertEmployeeSkus(["SKU-A", "SKU-C"], form, "bodega de empleado");
        form.LocationFilterForSmoke.SelectedValue = 3;
        AssertEmployeeSkus(["SKU-B"], form, "sin asignar de empleado");
        form.LocationFilterForSmoke.SelectedValue = 0;
        form.StockFilterForSmoke.SelectedValue = 2;
        AssertEmployeeSkus(["SKU-D"], form, "sin existencias de empleado");
        form.StockFilterForSmoke.SelectedValue = 0;
        form.SortFilterForSmoke.SelectedValue = 2;
        AssertEmployeeSkus(["SKU-D", "SKU-B", "SKU-C", "SKU-A"], form, "orden de alta de empleado");

        form.CategoryFilterForSmoke.SelectedValue = 1;
        form.SubcategoryFilterForSmoke.SelectedValue = 11;
        form.ImageFilterForSmoke.SelectedValue = 2;
        form.LocationFilterForSmoke.SelectedValue = 3;
        form.StockFilterForSmoke.SelectedValue = 2;
        form.SortFilterForSmoke.SelectedValue = 3;
        Assert(form.ShouldShowClearButtonForSmoke,
            "La X no aparece con filtros activos y búsqueda vacía en EMPLEADO.");
        form.SearchBoxForSmoke.Text = "SKU-A";
        form.ClearSearchForSmoke();
        form.CancelPendingSearchForSmoke();
        Assert(string.IsNullOrEmpty(form.SearchBoxForSmoke.Text), "La X no limpió la búsqueda de EMPLEADO.");
        Assert((int)form.CategoryFilterForSmoke.SelectedValue == 0 &&
               (int)form.SubcategoryFilterForSmoke.SelectedValue == 0 &&
               (int)form.ImageFilterForSmoke.SelectedValue == 0 &&
               (int)form.LocationFilterForSmoke.SelectedValue == 0 &&
               (int)form.StockFilterForSmoke.SelectedValue == 0 &&
               (int)form.SortFilterForSmoke.SelectedValue == 0,
            "La X no devolvió todos los filtros de EMPLEADO a sus valores predeterminados.");
        AssertEmployeeSkus(["SKU-A", "SKU-B", "SKU-C", "SKU-D"], form, "reiniciar catálogo de empleado");

        log.Add("Consulta de EMPLEADO: filtros, orden y X para restablecer búsqueda/filtros=OK");
    }

    private static EmployeeInventoryLookupItem EmployeeItem(
        string sku,
        string name,
        int categoryId,
        int? subcategoryId,
        string? subcategoryName,
        DateTime createdAt,
        int stock,
        int minimum,
        int exhibition,
        int warehouse,
        int unassigned,
        bool hasImage) => new(
            sku,
            sku,
            name,
            null,
            exhibition,
            0,
            warehouse,
            0,
            unassigned,
            0,
            hasImage ? ProductAssetOwnerKind.Product : null,
            hasImage ? 1 : null,
            null,
            null,
            categoryId,
            categoryId == 1 ? "Madera" : "Barro",
            subcategoryId,
            subcategoryName,
            200,
            createdAt,
            minimum,
            hasImage);

    private static void AssertEmployeeSkus(
        IReadOnlyList<string> expected,
        EmployeeInventoryLookupForm form,
        string filter)
    {
        var actual = form.VisibleSkuCodesForSmoke;
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"Filtro {filter}: esperado [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}].");
        }
    }

    private static void AssertIds(IReadOnlyList<int> expected, IReadOnlyList<int> actual, string filter)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"Filtro {filter}: esperado [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}].");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Smoke(Form form, float scale, string output, ICollection<string> log)
    {
        using (form)
        {
            if (scale != 1f) form.Scale(new SizeF(scale, scale));
            if (form is ProductImageBatchAssignmentForm && scale > 1f)
            {
                form.ClientSize = new Size(
                    (int)Math.Ceiling(1220 * scale),
                    (int)Math.Ceiling(760 * scale));
            }
            var hosted = form is ProductsForm && !form.TopLevel;
            var requestedSize = form.Size;
            form.Show();
            if (hosted) form.Size = requestedSize;
            System.Windows.Forms.Application.DoEvents(); form.PerformLayout();
            if (form is ProductsForm products)
            {
                products.StateFilterForSmoke.SelectedIndex = 1;
                System.Windows.Forms.Application.DoEvents();
                if (!products.ClearSearchButtonForSmoke.Visible || !products.ClearSearchButtonForSmoke.Enabled)
                {
                    throw new InvalidOperationException(
                        "ProductsForm: la X no aparece visualmente con filtros activos y búsqueda vacía.");
                }
                products.ClearSearchButtonForSmoke.PerformClick();
                products.CancelPendingSearchForSmoke();
                if (products.StateFilterForSmoke.SelectedIndex != 0)
                {
                    throw new InvalidOperationException("ProductsForm: la X visible no restableció los filtros.");
                }
                products.SetSearchTextForLayoutSmoke("smoke");
                System.Windows.Forms.Application.DoEvents();
                form.PerformLayout();
                ValidateProductsResponsiveLayout(products, scale);
            }
            else if (form is EmployeeInventoryLookupForm employeeInventory)
            {
                employeeInventory.CategoryFilterForSmoke.SelectedValue = 1;
                System.Windows.Forms.Application.DoEvents();
                if (!employeeInventory.ClearSearchButtonForSmoke.Visible ||
                    !employeeInventory.ClearSearchButtonForSmoke.Enabled)
                {
                    throw new InvalidOperationException(
                        "EmployeeInventoryLookupForm: la X no aparece visualmente con filtros activos y búsqueda vacía.");
                }
                employeeInventory.ClearSearchButtonForSmoke.PerformClick();
                employeeInventory.CancelPendingSearchForSmoke();
                if ((int)employeeInventory.CategoryFilterForSmoke.SelectedValue != 0)
                {
                    throw new InvalidOperationException(
                        "EmployeeInventoryLookupForm: la X visible no restableció los filtros.");
                }
            }
            else if (form is ProductImageBatchAssignmentForm imageBatch)
            {
                if (imageBatch.ImageFileCountForSmoke > 0)
                {
                    var previewLoad = imageBatch.SelectFirstImageRowForSmokeAsync();
                    while (!previewLoad.IsCompleted)
                    {
                        System.Windows.Forms.Application.DoEvents();
                        Thread.Sleep(1);
                    }
                    previewLoad.GetAwaiter().GetResult();
                    Assert(imageBatch.HasBothPreviewImagesForSmoke,
                        $"ProductImageBatchAssignmentForm: no se mostraron simultáneamente la imagen actual y la nueva. {imageBatch.PreviewDebugForSmoke}");
                    Assert(imageBatch.ImageTargetCellTextForSmoke(0).Contains("IMAGE-BATCH-SMOKE", StringComparison.Ordinal),
                        $"ProductImageBatchAssignmentForm: el destino revisado no se muestra con su nombre/código. {imageBatch.ImageTargetDebugForSmoke(0)}.");
                }
            }
            ValidateLayout(form, scale);
            ValidateInventoryDialogLayout(form, scale);
            SaveScreenshot(form, output);
            if (form is StockAdjustmentForm)
            {
                var countedQuantity = Descendants(form).OfType<NumericUpDown>().Single(control =>
                    control.AccessibleName == "Cantidad contada");
                var difference = Descendants(form).OfType<Label>().Single(control =>
                    control.AccessibleName == "Diferencia del ajuste");
                var originalCount = countedQuantity.Value;
                if (originalCount <= countedQuantity.Minimum)
                {
                    throw new InvalidOperationException("El smoke no puede preparar un conteo negativo de diferencia.");
                }

                countedQuantity.Value = originalCount - 1;
                System.Windows.Forms.Application.DoEvents();
                if (difference.Text != "-1" || difference.ForeColor != UITheme.DangerColor)
                {
                    throw new InvalidOperationException(
                        $"StockAdjustmentForm@{scale:P0}: la diferencia negativa no se mostró correctamente.");
                }

                SaveScreenshot(form, Path.Combine(Path.GetDirectoryName(output)!,
                    $"inventory-adjustment-negative-{ScaleName(scale)}.png"));
                countedQuantity.Value = originalCount;
            }

            form.Hide(); log.Add($"{form.GetType().Name}@{scale:P0}=OK;render={output}");
        }
    }

    private static void SmokeWorkspace(ProductWorkspaceForm form, float scale, string outputDirectory,
        ICollection<string> log)
    {
        using (form)
        {
            if (scale != 1f) form.Scale(new SizeF(scale, scale));
            form.Show(); System.Windows.Forms.Application.DoEvents(); form.PerformLayout();
            var tabs = Descendants(form).OfType<TabControl>().Single();
            var stableClientSize = form.ClientSize;
            for (var index = 0; index < tabs.TabCount; index++)
            {
                tabs.SelectedIndex = index;
                System.Windows.Forms.Application.DoEvents();
                form.PerformLayout();
                tabs.SelectedTab?.PerformLayout();
                ValidateLayout(form, scale);

                if (form.ClientSize != stableClientSize)
                {
                    throw new InvalidOperationException(
                        $"ProductWorkspaceForm: el tamaño de la ficha cambió al abrir '{tabs.SelectedTab?.Text}' a {scale:P0}; " +
                        $"esperado={stableClientSize}, actual={form.ClientSize}.");
                }

                var tabName = tabs.SelectedTab?.Text ?? $"tab-{index + 1}";
                if (tabName == "Inventario")
                {
                    var inventoryActions = Descendants(tabs.SelectedTab!).OfType<Button>()
                        .Where(button => button.Visible && button.Text is
                            "Transferir entre ubicaciones" or
                            "Asignar a una ubicación" or
                            "Ajustar existencias" or
                            "Registrar merma" or
                            "Configurar mínimo de exhibición")
                        .ToList();
                    var minimumButton = inventoryActions.SingleOrDefault(button =>
                        button.Text == "Configurar mínimo de exhibición");
                    var wasteButton = inventoryActions.SingleOrDefault(button => button.Text == "Registrar merma");
                    var actionRows = inventoryActions.Select(button => button.Top).Distinct().ToList();
                    if (inventoryActions.Count != 5 || minimumButton is null || wasteButton is null ||
                        actionRows.Count > 2 || minimumButton.Top != actionRows.Min() ||
                        minimumButton.Left <= wasteButton.Left || (scale <= 1.25f && actionRows.Count != 1))
                    {
                        throw new InvalidOperationException(
                            $"ProductWorkspaceForm/Inventario: acciones recortadas, filas inesperadas o botón de mínimo fuera del extremo derecho a {scale:P0}.");
                    }
                }
                if (tabName == "Imágenes")
                {
                    var preview = Descendants(tabs.SelectedTab!).OfType<PictureBox>().Single();
                    if (preview.ClientSize.Width < 240 || preview.ClientSize.Height < 220)
                        throw new InvalidOperationException(
                            $"{form.GetType().Name}/{tabName}: vista previa demasiado pequena {preview.ClientSize} a {scale:P0}.");
                }

                var output = Path.Combine(outputDirectory,
                    $"product-workspace-{ScaleName(scale)}-{TabName(tabName)}.png");
                SaveScreenshot(form, output);
                log.Add($"{form.GetType().Name}/{tabName}@{scale:P0}=OK;render={output}");
            }
            form.Hide();
        }
    }

    private static void ValidateLayout(Form form, float scale)
    {
        var buttons = Descendants(form).OfType<Button>()
            .Where(button => button.Visible && button.Text.Length > 2)
            .ToList();
        foreach (var button in buttons)
        {
            var measured = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (button.ClientSize.Width < measured.Width + 12 || button.ClientSize.Height < measured.Height + 6 ||
                button.TextAlign != ContentAlignment.MiddleCenter || button.AutoEllipsis)
                throw new InvalidOperationException(
                    $"{form.GetType().Name}/{button.Text}: contenido {measured} no cabe centrado en {button.ClientSize} a {scale:P0}.");
            if (button.Parent is not null && !button.Parent.ClientRectangle.Contains(button.Bounds))
                throw new InvalidOperationException(
                    $"{form.GetType().Name}/{button.Text}: boton fuera de su contenedor a {scale:P0}; " +
                    $"bounds={button.Bounds}, parent={button.Parent.GetType().Name}, " +
                    $"client={button.Parent.ClientRectangle}.");
        }

        foreach (var group in buttons.Where(button => button.Parent is not null).GroupBy(button => button.Parent))
        {
            var siblings = group.ToList();
            for (var first = 0; first < siblings.Count; first++)
            for (var second = first + 1; second < siblings.Count; second++)
                if (siblings[first].Bounds.IntersectsWith(siblings[second].Bounds))
                    throw new InvalidOperationException(
                        $"{form.GetType().Name}: '{siblings[first].Text}' se superpone con '{siblings[second].Text}' a {scale:P0}.");
        }

        foreach (var control in Descendants(form).Where(control => control.Visible))
        {
            if (control.Width < 0 || control.Height < 0)
                throw new InvalidOperationException($"{form.GetType().Name}: control con tamano invalido a {scale:P0}.");
        }
    }

    private static void ValidateInventoryDialogLayout(Form form, float scale)
    {
        var descendants = Descendants(form).Where(control => control.Visible).ToList();
        var expected = form switch
        {
            InventoryTransferDialog => (Combos: 2, Quantities: 1, Texts: 1, Checks: 0),
            InventoryWasteDialog => (Combos: 1, Quantities: 1, Texts: 1, Checks: 0),
            ExhibitionMinimumDialog => (Combos: 0, Quantities: 1, Texts: 0, Checks: 1),
            StockAdjustmentForm => (Combos: 1, Quantities: 1, Texts: 2, Checks: 0),
            _ => (Combos: -1, Quantities: -1, Texts: -1, Checks: -1)
        };
        if (expected.Combos < 0) return;

        if (descendants.OfType<ComboBox>().Count(control => !string.IsNullOrWhiteSpace(control.AccessibleName)) != expected.Combos ||
            descendants.OfType<NumericUpDown>().Count(control => !string.IsNullOrWhiteSpace(control.AccessibleName)) != expected.Quantities ||
            descendants.OfType<TextBox>().Count(control => !string.IsNullOrWhiteSpace(control.AccessibleName)) != expected.Texts ||
            descendants.OfType<CheckBox>().Count(control => !string.IsNullOrWhiteSpace(control.AccessibleName)) != expected.Checks)
        {
            throw new InvalidOperationException(
                $"{form.GetType().Name}@{scale:P0}: faltan controles de entrada visibles.");
        }

        foreach (var input in descendants.Where(control =>
                     (control is ComboBox or NumericUpDown or TextBox or CheckBox) &&
                     !string.IsNullOrWhiteSpace(control.AccessibleName)))
        {
            if (input.ClientSize.Width <= 0 || input.ClientSize.Height <= 0 ||
                input.Parent is null || !input.Parent.ClientRectangle.Contains(input.Bounds))
            {
                throw new InvalidOperationException(
                    $"{form.GetType().Name}/{input.AccessibleName}@{scale:P0}: control fuera del layout {input.Bounds}.");
            }
        }

        if (form is StockAdjustmentForm)
        {
            var reason = descendants.OfType<TextBox>().Single(control =>
                control.AccessibleName == "Motivo obligatorio");
            var password = descendants.OfType<TextBox>().Single(control =>
                control.AccessibleName == "Contraseña del usuario ADMIN");
            var countedQuantity = descendants.OfType<NumericUpDown>().Single(control =>
                control.AccessibleName == "Cantidad contada");
            var previousPhysical = descendants.OfType<Label>().Single(control =>
                control.AccessibleName == "Existencia anterior");
            var minimumFieldWidth = (int)(form.ClientSize.Width * 0.55f);
            if (!reason.Multiline || reason.Dock != DockStyle.Fill || reason.Width < minimumFieldWidth)
            {
                throw new InvalidOperationException(
                    $"StockAdjustmentForm/motivo@{scale:P0}: el campo no ocupa el ancho útil ({reason.Width}px). ");
            }

            if (password.Dock != DockStyle.Fill || password.Width < minimumFieldWidth)
            {
                throw new InvalidOperationException(
                    $"StockAdjustmentForm/contraseña@{scale:P0}: el campo no ocupa el ancho útil ({password.Width}px). ");
            }

            if (previousPhysical.Text != "30" || countedQuantity.Value != 30)
            {
                throw new InvalidOperationException(
                    $"StockAdjustmentForm/ubicación@{scale:P0}: el saldo inicial no se cargó desde Sin asignar " +
                    $"(anterior={previousPhysical.Text}; conteo={countedQuantity.Value}).");
            }
        }
    }

    private static void ValidateInventoryDialogRules()
    {
        var sku = BuildInventorySku();
        static void Expect(string expected, string? actual)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new InvalidOperationException($"Validación esperada '{expected}', actual '{actual ?? "<ninguna>"}'.");
        }

        Expect("Selecciona un SKU.", InventoryDialogValidator.ValidateSelectedSku(null));
        Expect("Selecciona una ubicación de origen.", InventoryDialogValidator.ValidateTransfer(
            sku, null, ArteInventariado.Domain.Enums.UbicacionInventario.EXHIBICION, 1, "Motivo", 21, false));
        Expect("Selecciona una ubicación de destino.", InventoryDialogValidator.ValidateTransfer(
            sku, ArteInventariado.Domain.Enums.UbicacionInventario.BODEGA, null, 1, "Motivo", 21, false));
        Expect("La ubicación de origen y destino deben ser diferentes.", InventoryDialogValidator.ValidateTransfer(
            sku, ArteInventariado.Domain.Enums.UbicacionInventario.BODEGA,
            ArteInventariado.Domain.Enums.UbicacionInventario.BODEGA, 1, "Motivo", 21, false));
        Expect("Ingresa una cantidad mayor que cero.", InventoryDialogValidator.ValidateTransfer(
            sku, ArteInventariado.Domain.Enums.UbicacionInventario.BODEGA,
            ArteInventariado.Domain.Enums.UbicacionInventario.EXHIBICION, 0, "Motivo", 21, false));
        Expect("Solo hay 21 unidades disponibles en Bodega.", InventoryDialogValidator.ValidateTransfer(
            sku, ArteInventariado.Domain.Enums.UbicacionInventario.BODEGA,
            ArteInventariado.Domain.Enums.UbicacionInventario.EXHIBICION, 22, "Motivo", 21, false));
        Expect("No hay inventario sin asignar para clasificar.", InventoryDialogValidator.ValidateTransfer(
            sku, ArteInventariado.Domain.Enums.UbicacionInventario.SIN_ASIGNAR,
            ArteInventariado.Domain.Enums.UbicacionInventario.EXHIBICION, 1, "Motivo", 0, true));
        Expect("Ingresa el motivo de la merma.", InventoryDialogValidator.ValidateWaste(
            sku, ArteInventariado.Domain.Enums.UbicacionInventario.BODEGA, 3, " ", 21));
        if (InventoryDialogValidator.ValidateTransfer(sku,
                ArteInventariado.Domain.Enums.UbicacionInventario.BODEGA,
                ArteInventariado.Domain.Enums.UbicacionInventario.EXHIBICION, 21,
                "Reposición", 21, false) is not null ||
            InventoryDialogValidator.ValidateWaste(sku,
                ArteInventariado.Domain.Enums.UbicacionInventario.BODEGA, 3,
                "Producto dañado", 21) is not null ||
            InventoryDialogValidator.ValidateMinimum(sku, inherit: false, minimum: 3) is not null)
        {
            throw new InvalidOperationException("Una operación válida fue rechazada por la validación de UI.");
        }
    }

    private static ProductSkuSummary BuildInventorySku() => new(
        364,
        364,
        "PRD-000364",
        null,
        true,
        true,
        3,
        null,
        null,
        1_000,
        100,
        1,
        21,
        30,
        0,
        52,
        52);

    private static void SaveScreenshot(Form form, string output)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static void ValidateProductsResponsiveLayout(
        ProductsForm form,
        float scale,
        bool validateClearButton = true)
    {
        var filters = form.FiltersForSmoke;
        var actions = form.ActionsForSmoke;
        var tabs = form.TabsForSmoke;
        var grid = form.GeneralGridForSmoke;
        var clearSearch = form.ClearSearchButtonForSmoke;
        var searchHost = form.SearchHostForSmoke;
        EnsureInside(form, filters, "filtros");
        EnsureInside(form, actions, "acciones");
        EnsureInside(form, tabs, "pestañas");
        EnsureInside(form, grid, "grilla");
        if (grid.Parent is not null) EnsureInside(grid.Parent, grid, "grilla en su página");
        if (filters.Bounds.Bottom > actions.Bounds.Top || actions.Bounds.Bottom > tabs.Bounds.Top)
            throw new InvalidOperationException($"ProductsForm: secciones superpuestas a {scale:P0}.");
        var maximumClearButtonHeight = (int)Math.Ceiling(UITheme.StandardControlHeight * scale) + 4;
        if (validateClearButton &&
            (!clearSearch.Visible || !clearSearch.Enabled ||
             clearSearch.Height > maximumClearButtonHeight ||
             clearSearch.Bounds.Bottom > searchHost.ClientRectangle.Bottom))
            throw new InvalidOperationException(
                $"ProductsForm: boton de limpiar busqueda demasiado alto a {scale:P0}; " +
                $"visible={clearSearch.Visible}, enabled={clearSearch.Enabled}, " +
                $"boton={clearSearch.Bounds}, fila={searchHost.ClientRectangle}.");

        foreach (Control field in filters.Controls)
        {
            if (!field.Visible) continue;
            EnsureInside(filters, field, "filtro");
            foreach (var child in Descendants(field).Where(control => control.Visible))
                EnsureInside(field, child, "control de filtro");
        }

        foreach (var button in actions.Controls.OfType<Button>().Where(button => button.Visible))
        {
            EnsureInside(actions, button, $"acción '{button.Text}'");
        }
    }

    private static void EnsureInside(Control parent, Control child, string description)
    {
        if (!parent.ClientRectangle.Contains(child.Bounds))
            throw new InvalidOperationException(
                $"{parent.GetType().Name}: {description} fuera de su contenedor {child.Bounds} / {parent.ClientRectangle}.");
    }

    private static void EnsureInside(Form form, Control child, string description)
    {
        var bounds = BoundsInAncestor(child, form);
        if (!form.ClientRectangle.Contains(bounds))
            throw new InvalidOperationException(
                $"ProductsForm: {description} fuera del área cliente {bounds} / {form.ClientRectangle}.");
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
        return new Rectangle(location, child.Size);
    }

    private static void Progress(string stage)
    {
        var path = Environment.GetEnvironmentVariable("ARTE_PRODUCT_LAYOUT_SMOKE_PROGRESS");
        if (string.IsNullOrWhiteSpace(path)) return;

        path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {stage}{Environment.NewLine}");
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static string ScaleName(float scale) => $"{scale * 100:N0}";
    private static string TabName(string name) => name switch
    {
        "General" => "general",
        "Variantes / SKU" => "sku",
        "Inventario" => "inventario",
        "Códigos de barras" => "barcodes",
        "Imágenes" => "imagenes",
        _ => "tab"
    };

    private sealed class PrincipalInstallationContext : IInstallationContext
    {
        public InstallationStatus Status { get; } = new(StationConfigurationState.Ready, InstallationMode.Principal,
            true, Guid.NewGuid(), 1, "smoke", null);
        public bool AllowsOperationalWrites => true;
        public void EnsureInitialized() { }
        public void EnsureOperationalWriteAllowed(string operationName) { }
    }

    private static void ValidateProductsDisposeIsIdempotent(ICollection<string> log)
    {
        var form = new ProductsForm(null!, null!, null!, null!, null!, null!, null!, null!,
            new PrincipalInstallationContext(), loadDataOnShow: false);
        var cancellationField = typeof(ProductsForm).GetField(
            "_previewCancellation",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("No se encontro la cancelacion de preview de Productos.");
        cancellationField.SetValue(form, new CancellationTokenSource());
        form.Dispose();
        form.Dispose();
        log.Add("ProductsForm doble Dispose=OK");
    }

    /// <summary>Dependencia inocua para abrir el diálogo de layout sin DB.</summary>
    private sealed class LayoutSmokeBarcodeService : IBarcodeService
    {
        public Task<IReadOnlyList<ProductSkuBarcodeSummary>> GetBySkuAsync(int productoSkuId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductSkuBarcodeSummary>>([]);

        public Task<BarcodeLookupResult?> LookupExactAsync(string codigo, CancellationToken cancellationToken = default) =>
            Task.FromResult<BarcodeLookupResult?>(null);

        public Task<IReadOnlyList<BarcodeSkuCandidate>> GetSkuCandidatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BarcodeSkuCandidate>>([]);

        public Task<GenerateMissingBarcodesResult> GenerateMissingBarcodesAsync(GenerateMissingBarcodesRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GenerateMissingBarcodesResult([]));

        public Task<ProductSkuBarcodeSummary> AssignAsync(AssignProductSkuBarcodeRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<ProductSkuBarcodeSummary>(new NotSupportedException("Smoke de layout."));

        public Task<ProductSkuBarcodeSummary> GenerateInternalAsync(GenerateProductSkuBarcodeRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<ProductSkuBarcodeSummary>(new NotSupportedException("Smoke de layout."));

        public Task SetPrincipalAsync(int productoSkuBarcodeId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetActiveAsync(int productoSkuBarcodeId, bool active, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class LayoutSmokeProductAssetService : IProductAssetService
    {
        public Task<IReadOnlyList<ProductAssetSummary>> GetByProductAsync(int productoId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductAssetSummary>>([]);
        public Task<ProductAssetSummary> ImportAsync(ImportProductAssetRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<ProductAssetSummary>(new NotSupportedException("Smoke de layout; no importa imágenes."));
        public Task SetPrimaryAsync(ProductAssetOwnerKind ownerKind, int assetId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(ProductAssetOwnerKind ownerKind, int assetId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> GetThumbnailPathAsync(ProductAssetOwnerKind ownerKind, int assetId, int maximumPixels = 400, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
        public Task<AssetCleanupResult> CleanupOrphansAsync(TimeSpan minimumAge, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AssetCleanupResult(0, 0, []));
    }

    private sealed class BatchSmokeProductAssetService : IProductAssetService
    {
        private readonly List<ProductAssetSummary> _assets = [];
        private int _nextAssetId = 100;

        public bool FailImports { get; init; }
        public int DeleteCount { get; private set; }
        public string? ThumbnailPath { get; init; }

        public ProductAssetSummary SeedPrimary(int productId, string fileName)
        {
            var asset = CreateAsset(_nextAssetId++, productId, fileName, primary: true);
            _assets.Add(asset);
            return asset;
        }

        public IReadOnlyList<ProductAssetSummary> ForProduct(int productId) =>
            _assets.Where(asset => asset.ProductoId == productId).ToList();

        public Task<IReadOnlyList<ProductAssetSummary>> GetByProductAsync(int productoId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductAssetSummary>>(ForProduct(productoId));

        public Task<ProductAssetSummary> ImportAsync(ImportProductAssetRequest request, CancellationToken cancellationToken = default)
        {
            if (FailImports)
                return Task.FromException<ProductAssetSummary>(new IOException("Importación simulada fallida."));

            var ownerKind = request.ProductoSkuId.HasValue ? ProductAssetOwnerKind.Sku : ProductAssetOwnerKind.Product;
            var ownerId = request.ProductoSkuId ?? request.ProductoId;
            var current = _assets.Where(asset => asset.OwnerKind == ownerKind && asset.OwnerId == ownerId).ToList();
            var setPrimary = request.SetAsPrimary || current.Count == 0;
            if (request.SetAsPrimary)
            {
                foreach (var asset in current)
                {
                    var index = _assets.FindIndex(candidate => candidate.AssetId == asset.AssetId);
                    _assets[index] = asset with { IsPrimary = false };
                }
            }

            var imported = CreateAsset(_nextAssetId++, request.ProductoId, Path.GetFileName(request.SourcePath), setPrimary,
                ownerKind, ownerId);
            _assets.Add(imported);
            return Task.FromResult(imported);
        }

        public Task SetPrimaryAsync(ProductAssetOwnerKind ownerKind, int assetId, CancellationToken cancellationToken = default)
        {
            for (var index = 0; index < _assets.Count; index++)
            {
                if (_assets[index].OwnerKind == ownerKind && _assets[index].OwnerId ==
                    _assets.First(asset => asset.AssetId == assetId).OwnerId)
                {
                    _assets[index] = _assets[index] with { IsPrimary = _assets[index].AssetId == assetId };
                }
            }
            return Task.CompletedTask;
        }

        public Task DeleteAsync(ProductAssetOwnerKind ownerKind, int assetId, CancellationToken cancellationToken = default)
        {
            _assets.RemoveAll(asset => asset.OwnerKind == ownerKind && asset.AssetId == assetId);
            DeleteCount++;
            return Task.CompletedTask;
        }

        public Task<string?> GetThumbnailPathAsync(ProductAssetOwnerKind ownerKind, int assetId, int maximumPixels = 400,
            CancellationToken cancellationToken = default) => Task.FromResult(ThumbnailPath);

        public Task<AssetCleanupResult> CleanupOrphansAsync(TimeSpan minimumAge, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AssetCleanupResult(0, 0, []));

        private static ProductAssetSummary CreateAsset(
            int assetId,
            int productId,
            string fileName,
            bool primary,
            ProductAssetOwnerKind ownerKind = ProductAssetOwnerKind.Product,
            int? ownerId = null) => new(
                assetId,
                ownerKind,
                ownerId ?? productId,
                productId,
                ownerKind == ProductAssetOwnerKind.Sku ? ownerId : null,
                Guid.NewGuid(),
                $"{(ownerKind == ProductAssetOwnerKind.Product ? "Products" : "Skus")}/{assetId}.png",
                fileName,
                "image/png",
                ".png",
                3,
                1,
                1,
                "smoke",
                0,
                primary,
                DateTime.UtcNow);
    }

    private sealed class LayoutSmokeEmployeePaymentService : IEmployeePaymentService
    {
        public Task<bool> CanRegisterAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanCancelAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<EmployeePaymentRecord> RegisterPaymentAsync(
            RegisterEmployeePaymentRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromException<EmployeePaymentRecord>(new NotSupportedException("Smoke de layout; no registra pagos."));
        public Task<EmployeePaymentRecord> CancelPaymentAsync(
            CancelEmployeePaymentRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromException<EmployeePaymentRecord>(new NotSupportedException("Smoke de layout; no cancela pagos."));
    }
}
