namespace MatGame.Pages;

/// <summary>What the sign-in panel of the portal shows (Shared/_AuthPanel.cshtml).</summary>
public sealed record AuthPanelModel(bool CanRegister, bool Guests, bool FirstRun, bool IdentityEnabled, string? ReturnUrl, string Tab, bool Closable = true);
