namespace ArteInventariado.Infrastructure;

public static class LocalApplicationDataPathProvider
{
    public const string VerificationRootEnvironmentVariable =
        ArteInventariadoPathProvider.VerificationRootEnvironmentVariable;

    public static string GetApplicationRoot()
    {
        if (Environment.GetEnvironmentVariable(ArteInventariadoPathProvider.VerificationModeEnvironmentVariable) == "1")
        {
            var verificationRoot = Environment.GetEnvironmentVariable(VerificationRootEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(verificationRoot))
            {
                return Path.GetFullPath(verificationRoot);
            }
        }

        return ArteInventariadoPathProvider.CreateLegacyDefault().MachineDataRoot;
    }
}
