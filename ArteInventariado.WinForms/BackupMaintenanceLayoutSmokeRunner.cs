namespace ArteInventariado.WinForms;

internal static class BackupMaintenanceLayoutSmokeRunner
{
    public static int Run(string? outputDirectory)
    {
        outputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(Path.GetTempPath(), "ArteInventariado.BackupLayoutSmoke")
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var logPath = Path.Combine(outputDirectory, "backup-layout-smoke.log");
        var lines = new List<string> { $"START={DateTimeOffset.UtcNow:O}" };

        try
        {
            using var settings = new SystemConfigForm(null!, null!, null!, null!, loadSettingsOnShow: false);
            ValidateAndRender(
                settings,
                new Size(900, 670),
                Path.Combine(outputDirectory, "system-config.png"),
                lines);

            using var cloudProvisioning = new CloudBackupProvisioningForm(
                null!,
                null!,
                loadExistingOnShow: false);
            ValidateCloudProvisioningSecurityControls(cloudProvisioning, lines);
            ValidateAndRender(
                cloudProvisioning,
                new Size(760, 560),
                Path.Combine(outputDirectory, "cloud-provisioning.png"),
                lines);

            using var cloudAuthorization = new CloudBackupProvisioningAuthorizationForm();
            ValidateAndRender(
                cloudAuthorization,
                new Size(620, 360),
                Path.Combine(outputDirectory, "cloud-provisioning-authorization.png"),
                lines);
            ValidateCloudAuthorizationLifecycle(lines);

            using var maintenance = new BackupRestoreForm(null!, null!, loadSettingsOnShow: false);
            ValidateAndRender(
                maintenance,
                new Size(1060, 720),
                Path.Combine(outputDirectory, "backup-restore.png"),
                lines);
            RenderTab(
                maintenance,
                1,
                Path.Combine(outputDirectory, "backup-restore-restore-tab.png"),
                lines);

            lines.Add("RESULT=OK");
            return 0;
        }
        catch (Exception ex)
        {
            lines.Add("RESULT=FAILED");
            lines.Add(ex.ToString());
            return 1;
        }
        finally
        {
            lines.Add($"END={DateTimeOffset.UtcNow:O}");
            File.WriteAllLines(logPath, lines);
        }
    }

    private static void ValidateCloudProvisioningSecurityControls(
        CloudBackupProvisioningForm form,
        ICollection<string> lines)
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var accessKey = (TextBox?)typeof(CloudBackupProvisioningForm)
            .GetField("_accessKey", flags)?.GetValue(form)
            ?? throw new InvalidOperationException("No se encontró el campo Access Key del aprovisionador cloud.");
        var secret = (TextBox?)typeof(CloudBackupProvisioningForm)
            .GetField("_secret", flags)?.GetValue(form)
            ?? throw new InvalidOperationException("No se encontró el campo Secret Access Key del aprovisionador cloud.");
        if (!accessKey.UseSystemPasswordChar || !secret.UseSystemPasswordChar)
        {
            throw new InvalidOperationException("Los campos de credencial cloud deben permanecer ocultos.");
        }

        using var authorization = new CloudBackupProvisioningAuthorizationForm();
        var password = (TextBox?)typeof(CloudBackupProvisioningAuthorizationForm)
            .GetField("_password", flags)?.GetValue(authorization)
            ?? throw new InvalidOperationException("No se encontró el campo de reautenticación cloud.");
        if (!password.UseSystemPasswordChar)
        {
            throw new InvalidOperationException("La contraseña de reautenticación cloud debe permanecer oculta.");
        }

        lines.Add("cloud-security-controls=OK");
    }

    private static void ValidateCloudAuthorizationLifecycle(ICollection<string> lines)
    {
        const string testPassword = "temporary-ui-secret";
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        using (var authorized = new CloudBackupProvisioningAuthorizationForm())
        {
            var password = (TextBox?)typeof(CloudBackupProvisioningAuthorizationForm)
                .GetField("_password", flags)?.GetValue(authorized)
                ?? throw new InvalidOperationException("No se encontró el campo de contraseña cloud.");
            var confirmation = (CheckBox?)typeof(CloudBackupProvisioningAuthorizationForm)
                .GetField("_confirmation", flags)?.GetValue(authorized)
                ?? throw new InvalidOperationException("No se encontró la confirmación cloud.");
            var continueButton = Descendants(authorized)
                .OfType<Button>()
                .Single(button => button.Text == "Continuar");

            authorized.Shown += (_, _) =>
            {
                password.Text = testPassword;
                confirmation.Checked = true;
                authorized.BeginInvoke(new Action(continueButton.PerformClick));
            };
            var result = authorized.ShowDialog();

            if (result != DialogResult.OK)
            {
                throw new InvalidOperationException("Continuar no cerró la reautenticación con resultado OK.");
            }

            if (password.Text.Length != 0)
            {
                throw new InvalidOperationException("OnFormClosed no limpió el TextBox de reautenticación cloud.");
            }

            var delivered = authorized.TakePassword();
            var expectedHash = BCrypt.Net.BCrypt.HashPassword(testPassword);
            if (!BCrypt.Net.BCrypt.Verify(delivered, expectedHash))
            {
                throw new InvalidOperationException("La contraseña entregada no valida contra BCrypt.");
            }

            if (authorized.TakePassword().Length != 0)
            {
                throw new InvalidOperationException("TakePassword no respeta la entrega one-shot.");
            }
        }

        using (var canceled = new CloudBackupProvisioningAuthorizationForm())
        {
            var password = (TextBox?)typeof(CloudBackupProvisioningAuthorizationForm)
                .GetField("_password", flags)?.GetValue(canceled)
                ?? throw new InvalidOperationException("No se encontró el campo de contraseña cloud.");
            var cancelButton = Descendants(canceled)
                .OfType<Button>()
                .Single(button => button.Text == "Cancelar");

            canceled.Shown += (_, _) =>
            {
                password.Text = testPassword;
                canceled.BeginInvoke(new Action(cancelButton.PerformClick));
            };
            var result = canceled.ShowDialog();

            if (result != DialogResult.Cancel || password.Text.Length != 0 || canceled.TakePassword().Length != 0)
            {
                throw new InvalidOperationException("Cancelar dejó un secreto utilizable o no limpió el TextBox.");
            }
        }

        lines.Add("cloud-reauth-lifecycle=OK");
    }

    private static void RenderTab(Form form, int selectedIndex, string imagePath, ICollection<string> lines)
    {
        var tabs = Descendants(form).OfType<TabControl>().Single();
        tabs.SelectedIndex = selectedIndex;
        form.Show();
        System.Windows.Forms.Application.DoEvents();
        form.PerformLayout();

        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
        form.Hide();
        lines.Add($"render={imagePath}");
    }

    private static void ValidateAndRender(
        Form form,
        Size clientSize,
        string imagePath,
        ICollection<string> lines)
    {
        form.ClientSize = clientSize;
        form.Show();
        System.Windows.Forms.Application.DoEvents();
        form.PerformLayout();
        foreach (var button in Descendants(form).OfType<Button>().Where(button => button.Text.Length > 2))
        {
            button.PerformLayout();
            var measured = TextRenderer.MeasureText(
                button.Text,
                button.Font,
                Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (button.ClientSize.Width < measured.Width + 16 ||
                button.ClientSize.Height < measured.Height + 8)
            {
                throw new InvalidOperationException(
                    $"{form.Name}/{button.Name}: contenido '{button.Text}' no cabe en {button.ClientSize}; requiere {measured}.");
            }

            if (button.TextAlign != ContentAlignment.MiddleCenter || button.AutoEllipsis)
            {
                throw new InvalidOperationException(
                    $"{form.Name}/{button.Name}: el boton no mantiene texto centrado y sin elipsis.");
            }

            lines.Add($"{form.GetType().Name}.{button.Name}={button.ClientSize.Width}x{button.ClientSize.Height};text={measured.Width}x{measured.Height}");
        }

        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
        form.Hide();
        lines.Add($"render={imagePath}");
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
