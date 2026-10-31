using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace ArteInventariado.WinForms;

/// <summary>
/// Data-free visual smoke for the administrative subcategory module. The
/// doubles return immutable in-memory data and never open the application DB.
/// </summary>
internal static class SubcategoryLayoutSmokeRunner
{
    private static readonly float[] Scales = [1f, 1.25f, 1.5f];

    internal static void ValidateWindowSizesForSmoke(string outputDirectory, ICollection<string> log)
    {
        Directory.CreateDirectory(outputDirectory);
        foreach (var scale in Scales)
        {
            RunCategorySmoke(scale, outputDirectory, log);
            RunDataSmoke(scale, outputDirectory, log);
        }
    }

    public static int Run(string? outputDirectory)
    {
        outputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(Path.GetTempPath(), "ArteInventariado.SubcategorySmoke")
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var log = new List<string> { $"START={DateTimeOffset.UtcNow:O}" };
        try
        {
            ValidateAdminSidebar(log);
            ValidateNavigationResolution(log);
            foreach (var scale in Scales)
            {
                RunCategorySmoke(scale, outputDirectory, log);
                RunDataSmoke(scale, outputDirectory, log);
                RunEmptySmoke(scale, outputDirectory, log);
                RunConsultaSmoke(scale, outputDirectory, log);
                RunEditSmoke(scale, outputDirectory, log);
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
            File.WriteAllLines(Path.Combine(outputDirectory, "subcategory-layout-smoke.log"), log);
        }
    }

    private static void ValidateAdminSidebar(ICollection<string> log)
    {
        var fixture = SmokeFixture.CreateEmpty(InstallationMode.Principal);
        using var dashboard = new AdminDashboardForm(
            authService: null!,
            currentSession: fixture.Session,
            creditAlertService: null!,
            commercialAlertService: null!,
            dashboardQueryService: null!,
            serviceProvider: null!,
            installationContext: fixture.Installation,
            installationManagement: null!);

        var buttons = dashboard.SidebarButtonsForSmoke
            .Select(button => button.Text)
            .ToList();
        var categoriesIndex = buttons.IndexOf("Categorias");
        var subcategoriesIndex = buttons.IndexOf("Subcategorías");
        var customersIndex = buttons.IndexOf("Clientes");
        Assert(categoriesIndex >= 0, "Categorias no aparece en el menú ADMIN.");
        Assert(subcategoriesIndex == categoriesIndex + 1, "Subcategorías no quedó debajo de Categorias.");
        Assert(customersIndex == subcategoriesIndex + 1, "Clientes no quedó después de Subcategorías.");

        var subcategoryButton = dashboard.SidebarButtonsForSmoke
            .Single(button => button.Text == "Subcategorías");
        Assert(!string.IsNullOrWhiteSpace(subcategoryButton.IconGlyph), "Subcategorías no tiene glyph.");
        Assert(dashboard.GetSidebarTooltipForSmoke(subcategoryButton) == "Subcategorías",
            "Subcategorías no tiene tooltip correcto.");

        dashboard.SetSidebarCollapsedForSmoke(true);
        Assert(subcategoryButton.Text.Length == 0, "El menú colapsado no ocultó el texto.");
        Assert(!string.IsNullOrWhiteSpace(subcategoryButton.IconGlyph) && subcategoryButton.CenterIcon,
            "El glyph de Subcategorías no se conservó al colapsar.");
        Assert(dashboard.GetSidebarTooltipForSmoke(subcategoryButton) == "Subcategorías",
            "El tooltip se perdió al colapsar el menú.");
        dashboard.SetSidebarCollapsedForSmoke(false);
        Assert(subcategoryButton.Text == "Subcategorías", "El menú expandido no restauró el texto.");
        log.Add("AdminDashboardForm menú Subcategorías/colapsado=OK");
    }

    private static void RunCategorySmoke(float scale, string outputDirectory, ICollection<string> log)
    {
        var fixture = SmokeFixture.CreateWithData(InstallationMode.Principal);
        using var form = new CategoriesForm(fixture.Categories);

        ShowAndPump(form, scale);
        PumpMessages();
        ValidateVisibleControls(form, scale);

        var buttonRows = Descendants(form).OfType<Button>()
            .Select(button => button.Top)
            .Distinct()
            .Count();
        Assert(buttonRows == 1, $"CategoriesForm: las acciones se dividieron en más de una fila a {scale:P0}.");

        SaveScreenshot(form, Path.Combine(outputDirectory, $"categories-default-{ScaleName(scale)}.png"));
        log.Add($"CategoriesForm tamaño inicial @{scale:P0}=OK; acciones en una fila; screenshot generado");
    }

    private static void RunDataSmoke(float scale, string outputDirectory, ICollection<string> log)
    {
        var fixture = SmokeFixture.CreateWithData(InstallationMode.Principal);
        using var form = new SubcategoriesForm(
            fixture.Subcategories,
            fixture.Categories,
            fixture.Installation,
            loadDataOnShow: false)
        {
            WindowState = FormWindowState.Normal
        };

        ShowAndPump(form, scale);
        form.RefreshForSmokeAsync().GetAwaiter().GetResult();
        PumpMessages();

        Assert(form.GridForSmoke.Rows.Count == 4, $"Se esperaban 4 filas con datos a {scale:P0}.");
        Assert(form.CategoryFilterForSmoke.Items.Count == 4, "El filtro no incluyó Todas y las tres categorías.");
        Assert(form.StatusFilterForSmoke.Items.Count == 4, "El filtro de estado no quedó completo.");
        Assert(form.CategoryFilterForSmoke.Text == "Todas las categorías", "Todas las categorías no es la opción inicial.");

        var states = Enumerable.Range(0, form.GridForSmoke.Rows.Count)
            .Select(index => form.GridForSmoke.Rows[index].Cells[3].Value?.ToString() ?? string.Empty)
            .ToList();
        Assert(states.Contains("Activa"), "No se mostró el estado activo asignable.");
        Assert(states.Contains("Activa · no asignable"), "No se distinguió el padre inactivo/protegido.");
        Assert(states.Contains("Inactiva"), "No se mostró el estado inactivo.");
        Assert(form.StatusLabelForSmoke.Text.Contains("4 subcategoría", StringComparison.Ordinal),
            "El estado inferior no reflejó las filas visibles.");
        Assert(new[]
            {
                form.NewButtonForSmoke.Top,
                form.EditButtonForSmoke.Top,
                form.ActivateButtonForSmoke.Top,
                form.DeactivateButtonForSmoke.Top,
                form.RefreshButtonForSmoke.Top
            }.Distinct().Count() == 1,
            $"SubcategoriesForm: las acciones se dividieron en más de una fila a {scale:P0}.");

        ValidateVisibleControls(form, scale);
        SaveScreenshot(form, Path.Combine(outputDirectory, $"subcategories-data-{ScaleName(scale)}.png"));
        log.Add($"SubcategoriesForm datos @ {scale:P0}=OK; menú/filtros/estados; screenshot generado");
    }

    private static void RunEmptySmoke(float scale, string outputDirectory, ICollection<string> log)
    {
        var fixture = SmokeFixture.CreateEmpty(InstallationMode.Principal);
        using var form = new SubcategoriesForm(
            fixture.Subcategories,
            fixture.Categories,
            fixture.Installation,
            loadDataOnShow: false)
        {
            WindowState = FormWindowState.Normal,
            ClientSize = new Size(900, 560)
        };

        ShowAndPump(form, scale);
        form.RefreshForSmokeAsync().GetAwaiter().GetResult();
        PumpMessages();

        Assert(form.GridForSmoke.Rows.Count == 0, "El estado vacío todavía contiene filas.");
        Assert(form.StatusLabelForSmoke.Text == "No hay subcategorías registradas.",
            "El mensaje de estado vacío no es claro.");
        ValidateVisibleControls(form, scale);
        SaveScreenshot(form, Path.Combine(outputDirectory, $"subcategories-empty-{ScaleName(scale)}.png"));
        log.Add($"SubcategoriesForm vacío @ {scale:P0}=OK; screenshot generado");
    }

    private static void RunConsultaSmoke(float scale, string outputDirectory, ICollection<string> log)
    {
        var fixture = SmokeFixture.CreateWithData(InstallationMode.Consulta);
        using var form = new SubcategoriesForm(
            fixture.Subcategories,
            fixture.Categories,
            fixture.Installation,
            loadDataOnShow: false)
        {
            WindowState = FormWindowState.Normal,
            ClientSize = new Size(900, 560)
        };

        ShowAndPump(form, scale);
        form.RefreshForSmokeAsync().GetAwaiter().GetResult();
        PumpMessages();

        Assert(form.ModeLabelForSmoke.Text.Contains("Modo Consulta", StringComparison.Ordinal),
            "No se identificó el modo Consulta.");
        Assert(!form.NewButtonForSmoke.Enabled, "Nueva quedó habilitada en Consulta.");
        Assert(!form.EditButtonForSmoke.Enabled, "Editar quedó habilitada en Consulta.");
        Assert(!form.ActivateButtonForSmoke.Enabled, "Activar quedó habilitada en Consulta.");
        Assert(!form.DeactivateButtonForSmoke.Enabled, "Desactivar quedó habilitada en Consulta.");
        Assert(form.RefreshButtonForSmoke.Enabled, "Actualizar se deshabilitó indebidamente en Consulta.");
        Assert(form.CategoryFilterForSmoke.Enabled && form.StatusFilterForSmoke.Enabled,
            "Los filtros se deshabilitaron indebidamente en Consulta.");

        ValidateVisibleControls(form, scale);
        SaveScreenshot(form, Path.Combine(outputDirectory, $"subcategories-consulta-{ScaleName(scale)}.png"));
        log.Add($"SubcategoriesForm CONSULTA @ {scale:P0}=OK; acciones escritura bloqueadas; screenshot generado");
    }

    private static void RunEditSmoke(float scale, string outputDirectory, ICollection<string> log)
    {
        var fixture = SmokeFixture.CreateWithData(InstallationMode.Principal);
        var summary = fixture.SubcategoriesData.Single(item => item.SubcategoriaId == 2);

        using var create = new SubcategoryEditForm(
            fixture.Subcategories,
            fixture.Categories,
            fixture.Installation,
            loadDataOnShow: false)
        {
            WindowState = FormWindowState.Normal,
            ClientSize = new Size(640, 470)
        };
        ShowAndPump(create, scale);
        create.LoadForSmokeAsync().GetAwaiter().GetResult();
        PumpMessages();
        Assert(create.CategorySelectorForSmoke.Items.Count == 1,
            "Crear no excluyó categorías inactivas y Sin categoria.");
        Assert(create.CategorySelectorForSmoke.Enabled, "La categoría quedó bloqueada en modo crear.");
        Assert(create.SaveButtonForSmoke.Enabled, "Guardar quedó deshabilitado con una categoría válida.");
        ValidateVisibleControls(create, scale);
        SaveScreenshot(create, Path.Combine(outputDirectory, $"subcategory-create-{ScaleName(scale)}.png"));
        create.Close();

        using var edit = new SubcategoryEditForm(
            fixture.Subcategories,
            fixture.Categories,
            fixture.Installation,
            summary,
            loadDataOnShow: false)
        {
            WindowState = FormWindowState.Normal,
            ClientSize = new Size(640, 470)
        };
        ShowAndPump(edit, scale);
        edit.LoadForSmokeAsync().GetAwaiter().GetResult();
        PumpMessages();
        Assert(!edit.CategorySelectorForSmoke.Enabled, "La categoría padre quedó editable en modo editar.");
        Assert(edit.CategorySelectorForSmoke.Text == "Madera", "Editar no mostró la categoría padre actual.");
        Assert(edit.NameInputForSmoke.Text == summary.Nombre, "Editar no cargó el nombre actual.");
        ValidateVisibleControls(edit, scale);
        SaveScreenshot(edit, Path.Combine(outputDirectory, $"subcategory-edit-{ScaleName(scale)}.png"));
        edit.Close();
        log.Add($"SubcategoryEditForm crear/editar @ {scale:P0}=OK; screenshot generado");
    }

    private static void ValidateNavigationResolution(ICollection<string> log)
    {
        var fixture = SmokeFixture.CreateEmpty(InstallationMode.Principal);
        using var owner = new Form
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(10, 10),
            Size = new Size(240, 160),
            ShowInTaskbar = false
        };
        owner.Show();
        PumpMessages();

        SubcategoriesForm? opened = null;
        // The coordinator resolves the same registered form as production.
        // Closing it from Shown keeps this check data-free and non-interactive.
        var provider = new ServiceCollection()
            .AddSingleton<ISubcategoryService>(fixture.Subcategories)
            .AddSingleton<ICategoryService>(fixture.Categories)
            .AddTransient<SubcategoriesForm>(_ =>
            {
                var form = new SubcategoriesForm(
                    fixture.Subcategories,
                    fixture.Categories,
                    fixture.Installation,
                    loadDataOnShow: false);
                opened = form;
                form.Shown += (_, _) => form.BeginInvoke(form.Close);
                return form;
            })
            .BuildServiceProvider();
        using (provider)
        {
            var coordinator = new AdminNavigationCoordinator(provider, fixture.Session, fixture.Installation);
            coordinator.Open(AdminModule.Subcategories, owner);
        }

        Assert(opened is not null, "La navegación no resolvió SubcategoriesForm.");
        log.Add("AdminNavigationCoordinator.Subcategories=OK");
    }

    private static void ShowAndPump(Form form, float scale)
    {
        if (scale != 1f)
        {
            form.Scale(new SizeF(scale, scale));
        }

        form.Show();
        PumpMessages();
        form.PerformLayout();
    }

    private static void ValidateVisibleControls(Control root, float scale)
    {
        foreach (var control in Descendants(root).Where(control => control.Visible))
        {
            if (control.Width <= 0 || control.Height <= 0)
            {
                throw new InvalidOperationException(
                    $"{root.GetType().Name}: control {control.GetType().Name} ('{control.Name}') sin área visible a {scale:P0}; bounds={control.Bounds}; parent={control.Parent?.GetType().Name} {control.Parent?.ClientRectangle}.");
            }

            if (control.Parent is not null && !control.Parent.ClientRectangle.Contains(control.Bounds))
            {
                throw new InvalidOperationException(
                    $"{root.GetType().Name}: control {control.GetType().Name} fuera del contenedor a {scale:P0}; bounds={control.Bounds}; parent={control.Parent.ClientRectangle}; autosize={control.AutoSize}; min={control.MinimumSize}; dock={control.Dock}.");
            }
        }

        foreach (var button in Descendants(root).OfType<Button>().Where(button => button.Visible))
        {
            var measured = TextRenderer.MeasureText(
                button.Text,
                button.Font,
                Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (button.ClientSize.Width < measured.Width + 12 ||
                button.ClientSize.Height < measured.Height + 6 ||
                button.TextAlign != ContentAlignment.MiddleCenter ||
                button.AutoEllipsis)
            {
                throw new InvalidOperationException(
                    $"{root.GetType().Name}/{button.Text}: contenido no cabe a {scale:P0}.");
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

    private static void SaveScreenshot(Form form, string path)
    {
        using var bitmap = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height));
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static void PumpMessages()
    {
        System.Windows.Forms.Application.DoEvents();
        Thread.Sleep(20);
        System.Windows.Forms.Application.DoEvents();
    }

    private static string ScaleName(float scale) => scale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
        .Replace('.', '_');

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class SmokeFixture
    {
        private SmokeFixture(
            ISubcategoryService subcategories,
            ICategoryService categories,
            SmokeInstallationContext installation,
            SmokeSession session,
            IReadOnlyList<CategorySummary> categoryData,
            IReadOnlyList<SubcategorySummary> subcategoryData)
        {
            Subcategories = subcategories;
            Categories = categories;
            Installation = installation;
            Session = session;
            CategoryData = categoryData;
            SubcategoriesData = subcategoryData;
        }

        public ISubcategoryService Subcategories { get; }
        public ICategoryService Categories { get; }
        public SmokeInstallationContext Installation { get; }
        public SmokeSession Session { get; }
        public IReadOnlyList<CategorySummary> CategoryData { get; }
        public IReadOnlyList<SubcategorySummary> SubcategoriesData { get; }

        public static SmokeFixture CreateWithData(InstallationMode mode)
        {
            var categories = new List<CategorySummary>
            {
                new(1, "Barro negro", "", true),
                new(2, "Madera", "", false),
                new(3, "Sin categoria", null, true)
            };
            var subcategories = new List<SubcategorySummary>
            {
                new(1, 1, "Ollas", "Piezas para cocina", true),
                new(2, 2, "Tallado", "Registro histórico", true),
                new(3, 1, "Cazuelas", null, false),
                new(4, 3, "Legado", "Registro no asignable", true)
            };
            return Create(mode, categories, subcategories);
        }

        public static SmokeFixture CreateEmpty(InstallationMode mode)
        {
            var categories = new List<CategorySummary>
            {
                new(1, "Barro negro", null, true),
                new(2, "Sin categoria", null, true)
            };
            return Create(mode, categories, []);
        }

        private static SmokeFixture Create(
            InstallationMode mode,
            IReadOnlyList<CategorySummary> categories,
            IReadOnlyList<SubcategorySummary> subcategories)
        {
            var session = new SmokeSession();
            var installation = new SmokeInstallationContext(mode);
            var subcategoryService = new SmokeSubcategoryService(subcategories);
            var categoryService = new SmokeCategoryService(categories);
            return new SmokeFixture(
                subcategoryService,
                categoryService,
                installation,
                session,
                categories,
                subcategories);
        }
    }

    private sealed class SmokeSubcategoryService(IReadOnlyList<SubcategorySummary> data) : ISubcategoryService
    {
        public Task<IReadOnlyList<SubcategorySummary>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(data);

        public Task<IReadOnlyList<SubcategorySummary>> GetActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(data.Where(item => item.Activa).ToList() as IReadOnlyList<SubcategorySummary>);

        public Task<IReadOnlyList<SubcategorySummary>> GetByCategoryAsync(int categoriaId, bool soloActivas = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(data.Where(item => item.CategoriaId == categoriaId && (!soloActivas || item.Activa)).ToList() as IReadOnlyList<SubcategorySummary>);

        public Task<SubcategorySummary?> GetByIdAsync(int subcategoriaId, CancellationToken cancellationToken = default) =>
            Task.FromResult(data.FirstOrDefault(item => item.SubcategoriaId == subcategoriaId));

        public Task<SubcategorySummary> CreateAsync(CreateSubcategoryRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<SubcategorySummary>(new NotSupportedException("Smoke visual."));

        public Task<SubcategorySummary> UpdateAsync(UpdateSubcategoryRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<SubcategorySummary>(new NotSupportedException("Smoke visual."));

        public Task SetActiveAsync(int subcategoriaId, bool activa, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(int subcategoriaId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SmokeCategoryService(IReadOnlyList<CategorySummary> data) : ICategoryService
    {
        public Task<IReadOnlyList<CategorySummary>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(data);

        public Task<IReadOnlyList<CategorySummary>> GetActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(data.Where(item => item.Activa).ToList() as IReadOnlyList<CategorySummary>);

        public Task<CategorySummary> GetDefaultCategoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<CategorySummary>(new NotSupportedException("Smoke visual."));

        public Task<CategorySummary> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<CategorySummary>(new NotSupportedException("Smoke visual."));

        public Task<CategorySummary> UpdateAsync(UpdateCategoryRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<CategorySummary>(new NotSupportedException("Smoke visual."));

        public Task SetActiveAsync(int categoriaId, bool activa, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(int categoriaId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SmokeInstallationContext(InstallationMode mode) : IInstallationContext
    {
        public InstallationStatus Status { get; } = new(
            StationConfigurationState.Ready,
            mode,
            true,
            Guid.NewGuid(),
            1,
            "visual-smoke",
            null);

        public bool AllowsOperationalWrites => mode == InstallationMode.Principal;
        public void EnsureInitialized() { }
        public void EnsureOperationalWriteAllowed(string operationName)
        {
            if (!AllowsOperationalWrites)
            {
                throw new InvalidOperationException("Modo Consulta: escritura no permitida.");
            }
        }
    }

    private sealed class SmokeSession : ICurrentSession
    {
        public UsuarioSesion? UsuarioActual { get; private set; } = new(
            1,
            "Administrador de smoke",
            "admin",
            RolUsuario.ADMIN);

        public bool EstaAutenticado => UsuarioActual is not null;
        public bool EsAdmin => UsuarioActual?.Rol == RolUsuario.ADMIN;
        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
    }
}
