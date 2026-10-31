using System.Diagnostics;

namespace ArteInventariado.WinForms;

internal static class ApplicationVersion
{
    public const string UnavailableProductVersion = "Versión no disponible";

    public static string ProductVersion
    {
        get
        {
            try
            {
                var executablePath = System.Windows.Forms.Application.ExecutablePath;
                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                {
                    return UnavailableProductVersion;
                }

                var productVersion = FileVersionInfo.GetVersionInfo(executablePath).ProductVersion;
                return string.IsNullOrWhiteSpace(productVersion)
                    ? UnavailableProductVersion
                    : productVersion.Trim();
            }
            catch
            {
                return UnavailableProductVersion;
            }
        }
    }

    public static string LoginText
    {
        get
        {
            var productVersion = ProductVersion;
            return productVersion == UnavailableProductVersion
                ? $"ArteInventariado · {productVersion}"
                : $"ArteInventariado v{productVersion}";
        }
    }
}
