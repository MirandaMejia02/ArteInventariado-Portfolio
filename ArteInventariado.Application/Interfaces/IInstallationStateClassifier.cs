using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Interfaces;

/// <summary>
/// Classifies installation evidence without initializing the business
/// database.  H2 uses this boundary before it offers New vs Recovery.
/// </summary>
public interface IInstallationStateClassifier
{
    InstallationStateAssessment Classify();
}
