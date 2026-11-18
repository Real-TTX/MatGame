namespace MatGame.Data;

/// <summary>The state of a record that supports soft delete (0 = Deleted, 1 = Created, 2 = Updated).</summary>
public enum UpdateState : short
{
    Deleted = 0,
    Created = 1,
    Updated = 2,
}

/// <summary>Every record has an Id (bigint) and the four audit columns.</summary>
public abstract class Entity
{
    public long Id { get; set; }

    public DateTime CreateDate { get; set; }

    public long? CreateUserId { get; set; }

    public DateTime UpdateDate { get; set; }

    public long? UpdateUserId { get; set; }
}

/// <summary>A record that is never removed from the table, only marked as deleted.</summary>
public abstract class SoftDeleteEntity : Entity
{
    public UpdateState UpdateState { get; set; } = UpdateState.Created;
}

/// <summary>An account of MatGame: plays every game of the master. A guest has no password and one device (or the browser's cookie).</summary>
public sealed class Account : SoftDeleteEntity
{
    /// <summary>The name to sign in with (unique, compared without case). Guests get a generated one ("gast-…").</summary>
    public string LoginName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>ASP.NET Core PasswordHasher (v3); empty for guests and for accounts that only sign in through MatIdentity.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsGuest { get; set; }

    /// <summary>The <c>sub</c> of MatIdentity when the account is linked (null = local only).</summary>
    public string? IdentitySubject { get; set; }

    public DateTime? LastLoginDate { get; set; }
}

/// <summary>A device of an account (an app): signs in with its token (only the SHA-256 is kept).</summary>
public sealed class Device : Entity
{
    public long AccountId { get; set; }

    public Account? Account { get; set; }

    /// <summary>"BunnyWarfare · Windows".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>windows, android, ios, macos.</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>The game the device signed in from (bunnywarfare).</summary>
    public string Game { get; set; } = string.Empty;

    /// <summary>SHA-256 of the token (hex).</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime LastUsedDate { get; set; }
}

/// <summary>A sign-in of a device in the browser: the app shows <see cref="Code"/>, the player confirms it at /link, the app collects its token.</summary>
public sealed class DeviceLink : Entity
{
    /// <summary>"ABCD-EFGH" (shown to the player).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>SHA-256 of the secret the app polls with.</summary>
    public string DeviceCodeHash { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public string Game { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    /// <summary>Set when the player confirmed (the account that signs the device in).</summary>
    public long? ApprovedAccountId { get; set; }

    public bool Denied { get; set; }

    /// <summary>True once the app has collected its token (a code is used once).</summary>
    public bool Collected { get; set; }
}

/// <summary>A setting of the master (key and value).</summary>
public sealed class Setting : Entity
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

/// <summary>What the administration decided about a game (plugin): on or off, and the oldest app that may play online.</summary>
public sealed class GameSetting : Entity
{
    public string GameId { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Overrides the game's own minimum app version (null = the game's).</summary>
    public string? MinClient { get; set; }
}
