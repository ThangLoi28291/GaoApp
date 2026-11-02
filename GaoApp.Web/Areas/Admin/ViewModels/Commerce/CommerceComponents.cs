namespace GaoApp.Web.Areas.Admin.ViewModels.Commerce;

/// <summary>Presentation only. These components do not authorize or execute business actions.</summary>
public sealed record CommerceHeader(string Title, string? Context = null, string? Description = null);

public enum CommerceTone { Neutral, Success, Info, Warning, Danger }

public sealed record CommerceStatus(string Text, CommerceTone Tone = CommerceTone.Neutral);

public enum CommerceStateKind { Empty, Loading, Error }

public sealed record CommerceState(CommerceStateKind Kind, string Title, string Description);

/// <summary>Caller supplies already authorized, configured items. This component never creates menus.</summary>
public sealed record CommerceNavigationItem(string Key, string Label, string IconId, string TargetId, bool OpensDialog = false);

public sealed record CommerceNavigation(IReadOnlyList<CommerceNavigationItem> Items, string MenuDialogId, string ActiveKey);
