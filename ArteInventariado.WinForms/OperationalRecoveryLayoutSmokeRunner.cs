using ArteInventariado.Application.Models;
using ArteInventariado.Application.Services;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

internal static class OperationalRecoveryLayoutSmokeRunner
{
    public static int Run(string? outputDirectory)
    {
        outputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(Path.GetTempPath(), "ArteInventariado.OperationalRecoveryLayoutSmoke")
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var log = new List<string>();
        try
        {
            var cash = new CashSummary(
                42,
                new DateOnly(2026, 8, 14),
                EstadoCaja.ABIERTA,
                10_000,
                5_000,
                500,
                250,
                0,
                14_250,
                null,
                new DateTime(2026, 8, 14, 14, 30, 0, DateTimeKind.Utc),
                null,
                "admin",
                null);
            var status = new CashRecoveryStatus(cash, true, 2);
            foreach (var scale in new[] { 1f, 1.25f, 1.5f })
            {
                Smoke(
                    new CashRecoveryForm(status, canContinue: true, canRegularize: true, readOnly: false),
                    scale,
                    Path.Combine(outputDirectory, $"cash-recovery-{ScaleName(scale)}.png"),
                    log);
                Smoke(
                    new CashRegularizationForm(cash),
                    scale,
                    Path.Combine(outputDirectory, $"cash-regularization-{ScaleName(scale)}.png"),
                    log);
                Smoke(
                    new PosCartExitConfirmationForm(new PosCartExitSummary(3, 5), PosExitReason.ApplicationExit),
                    scale,
                    Path.Combine(outputDirectory, $"pos-cart-exit-{ScaleName(scale)}.png"),
                    log);
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
            File.WriteAllLines(Path.Combine(outputDirectory, "operational-recovery-layout-smoke.log"), log);
        }
    }

    private static void Smoke(Form form, float scale, string imagePath, ICollection<string> log)
    {
        using (form)
        {
            if (scale != 1f)
            {
                form.Scale(new SizeF(scale, scale));
            }

            form.Show();
            System.Windows.Forms.Application.DoEvents();
            form.PerformLayout();
            foreach (var button in Descendants(form).OfType<Button>().Where(button => button.Text.Length > 2))
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
                        $"{form.GetType().Name}/{button.Text}: contenido {measured} no cabe centrado en {button.ClientSize} a escala {scale}.");
                }
            }

            foreach (var label in Descendants(form).OfType<Label>().Where(label => label.Text.Length > 2))
            {
                var measured = TextRenderer.MeasureText(
                    label.Text,
                    label.Font,
                    new Size(Math.Max(1, label.ClientSize.Width), 10_000),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                if (label.ClientSize.Height + 2 < measured.Height)
                {
                    throw new InvalidOperationException(
                        $"{form.GetType().Name}/Label: texto '{label.Text[..Math.Min(40, label.Text.Length)]}' requiere {measured.Height}px y dispone de {label.ClientSize.Height}px a escala {scale}.");
                }
            }

            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
            form.Hide();
            log.Add($"{form.GetType().Name}@{scale:N2}=OK;render={imagePath}");
        }
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

    private static string ScaleName(float scale) => $"{scale * 100:N0}";
}
