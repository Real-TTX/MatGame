using MatGame.Business;
using MatGame.Sdk;
using MatGame.Tests.SampleGame;
using MatGame.Web;

namespace MatGame.Tests;

/// <summary>Rules without the host: versions, the client header, codes and tokens, the choice of games.</summary>
public sealed class RulesTests
{
    [Theory]
    [InlineData("1.4.12-20261118", "1.4.0", true)]
    [InlineData("1.4.0-20261118", "1.4.12", false)]
    [InlineData("2.0.0-20261118", "1.9", true)]
    [InlineData("0.9.3-20261118", "1.0", false)]
    [InlineData("nightly-42-20261118", "9.0", true)]
    [InlineData("local-20261118", "9.0", true)]
    [InlineData("1.0.0", null, true)]
    [InlineData(null, "1.0", false)]
    public void Versions_compare_like_the_family_writes_them(string? version, string? minimum, bool ok)
        => Assert.Equal(ok, GameVersion.AtLeast(version, minimum));

    [Fact]
    public void The_client_header_names_game_version_and_platform()
    {
        Assert.Equal(("bunnywarfare", "1.4.0", "windows"), MatGameProtocol.ParseClient("bunnywarfare/1.4.0 (windows)"));
        Assert.Equal(("kingbee", null, null), MatGameProtocol.ParseClient("kingbee"));
        Assert.Equal(((string?)null, (string?)null, (string?)null), MatGameProtocol.ParseClient(" "));
    }

    [Fact]
    public void Codes_have_eight_letters_without_look_alikes_and_are_read_loosely()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = DeviceLogic.NewCode();
            Assert.Matches("^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$", code);
        }

        Assert.Equal("ABCD-EFGH", DeviceLogic.NormalizeCode(" abcd efgh "));
        Assert.Null(DeviceLogic.NormalizeCode("ABC"));
    }

    [Fact]
    public void Tokens_are_random_and_only_their_hash_is_kept()
    {
        var a = DeviceLogic.NewToken();
        var b = DeviceLogic.NewToken();
        Assert.NotEqual(a, b);
        Assert.Equal(43, a.Length);
        Assert.Equal(64, DeviceLogic.Hash(a).Length);
        Assert.Equal(DeviceLogic.Hash(a), DeviceLogic.Hash(a));
        Assert.NotEqual(DeviceLogic.Hash(a), DeviceLogic.Hash(b));
    }

    [Fact]
    public void A_token_expires_after_half_a_year_without_use()
    {
        var now = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(DeviceLogic.IsExpired(now.AddDays(-179), now));
        Assert.True(DeviceLogic.IsExpired(now.AddDays(-181), now));
    }

    [Fact]
    public void MATGAME_GAMES_chooses_the_games()
    {
        IGameModule[] all = [new SampleModule()];
        Assert.Single(GameHost.Choose(all, null));
        Assert.Single(GameHost.Choose(all, "sample, other"));
        Assert.Empty(GameHost.Choose(all, "kingbee"));
    }

    [Fact]
    public void The_assets_of_a_game_live_below_its_path()
    {
        var game = new HostedGame(new SampleModule(), new GameEnvironment("", "", "/sample", false));
        Assert.Equal("/sample/img/cover.svg", GameHost.Asset(game, "img/cover.svg"));
        Assert.Equal("/elsewhere.png", GameHost.Asset(game, "/elsewhere.png"));
        Assert.Null(GameHost.Asset(game, null));
    }

    [Fact]
    public void The_permission_checks_refuse_what_is_not_allowed()
    {
        Assert.Throws<AccessDeniedException>(() => Guard.Admin(new Actor(5, "Hoppel", false)));
        Assert.Throws<AccessDeniedException>(() => Guard.SignedIn(Actor.Anonymous));
        Assert.Throws<AccessDeniedException>(() => Guard.AdminOrSelf(new Actor(5, "Hoppel", false), 6));
        Guard.AdminOrSelf(new Actor(5, "Hoppel", false), 5);
        Guard.Admin(Actor.System);
    }
}
