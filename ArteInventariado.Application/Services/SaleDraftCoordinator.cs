using ArteInventariado.Application.Models;

namespace ArteInventariado.Application.Services;

/// <summary>
/// Coordina hasta cinco ventas abiertas en memoria. No reserva stock, no abre
/// DbContexts y no persiste nada: la validación final pertenece a ISaleService.
/// </summary>
public sealed class SaleDraftCoordinator
{
    public const int DefaultMaximumDrafts = 5;

    private readonly int _maximumDrafts;
    private readonly List<SaleDraft> _drafts = [];
    private int _nextTitleNumber = 1;

    public SaleDraftCoordinator(int maximumDrafts = DefaultMaximumDrafts)
    {
        if (maximumDrafts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDrafts));
        }

        _maximumDrafts = maximumDrafts;
        var initial = CreateDraftCore();
        _drafts.Add(initial);
        ActiveDraft = initial;
    }

    public int MaximumDrafts => _maximumDrafts;

    public IReadOnlyList<SaleDraft> Drafts => _drafts;

    public SaleDraft ActiveDraft { get; private set; }

    public bool CanCreateDraft => _drafts.Count < _maximumDrafts;

    public bool TryCreateDraft(out SaleDraft draft)
    {
        if (!CanCreateDraft)
        {
            draft = ActiveDraft;
            return false;
        }

        draft = CreateDraftCore();
        _drafts.Add(draft);
        ActiveDraft = draft;
        return true;
    }

    public bool TryActivate(Guid draftId)
    {
        var draft = _drafts.FirstOrDefault(item => item.Id == draftId);
        if (draft is null)
        {
            return false;
        }

        ActiveDraft = draft;
        return true;
    }

    /// <summary>
    /// Cierra un draft. Si era el último, se conserva un draft vacío para que
    /// el POS siempre tenga una venta activa disponible.
    /// </summary>
    public bool TryClose(Guid draftId, bool discardConfirmed)
    {
        var draft = _drafts.FirstOrDefault(item => item.Id == draftId);
        if (draft is null || (draft.Lines.Count > 0 && !discardConfirmed))
        {
            return false;
        }

        if (_drafts.Count == 1)
        {
            draft.Reset();
            ActiveDraft = draft;
            return true;
        }

        var wasActive = ReferenceEquals(draft, ActiveDraft);
        var index = _drafts.IndexOf(draft);
        _drafts.RemoveAt(index);
        if (wasActive)
        {
            ActiveDraft = _drafts[Math.Clamp(index - 1, 0, _drafts.Count - 1)];
        }

        return true;
    }

    public void ResetAll()
    {
        foreach (var draft in _drafts.Skip(1).ToList())
        {
            _drafts.Remove(draft);
        }

        _drafts[0].Reset();
        ActiveDraft = _drafts[0];
    }

    private SaleDraft CreateDraftCore()
    {
        var title = $"Venta {_nextTitleNumber++}";
        return new SaleDraft(title);
    }
}
