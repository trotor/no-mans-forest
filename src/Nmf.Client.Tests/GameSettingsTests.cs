using Nmf.Client;

namespace Nmf.Client.Tests;

public class GameSettingsTests
{
    [Fact]
    public void Defaults()
    {
        var s = new GameSettings();
        Assert.Equal((80, 100, 100, 70, 60), (s.Master, s.Sfx, s.Voice, s.Ambience, s.Music));
        Assert.False(s.Mute);
        Assert.False(s.SoundMarks);
        Assert.Null(s.Language);
        Assert.False(s.Fullscreen);
        Assert.Equal(100, s.UiScalePct);
        Assert.Equal("small", s.CardSize);
        Assert.False(s.FollowAtStart);
    }

    [Fact]
    public void RoundTrip()
    {
        var s = new GameSettings { Master = 55, Music = 0, Mute = true, SoundMarks = true, Language = "fi", Fullscreen = true, UiScalePct = 150, CardSize = "icon", FollowAtStart = true };
        Assert.Equal(s, GameSettings.FromJson(s.ToJson()));
    }

    [Fact]
    public void BrokenJson_GivesDefaults() => Assert.Equal(new GameSettings(), GameSettings.FromJson("{ not json"));

    [Fact]
    public void PartialAndOutOfRange_FallBackAndClamp()
    {
        var s = GameSettings.FromJson("""{ "Master": 140, "Sfx": -5, "UiScalePct": 333, "CardSize": "huge", "Language": "de", "Unknown": 1 }""");
        Assert.Equal(100, s.Master);
        Assert.Equal(0, s.Sfx);
        Assert.Equal(100, s.Voice);
        Assert.Equal(100, s.UiScalePct);
        Assert.Equal("small", s.CardSize);
        Assert.Null(s.Language);
    }

    [Theory]
    [InlineData(100, 0.0)]
    [InlineData(50, -6.02)]
    [InlineData(0, -80.0)]
    public void VolumeDb_ZeroIsSilent_HundredIsZeroDb(int pct, double db) => Assert.Equal(db, GameSettings.VolumeDb(pct), 2);

    [Theory]
    [InlineData(false, false, 80, false, false)]
    [InlineData(true, false, 80, false, true)]
    [InlineData(false, true, 80, false, true)]
    [InlineData(false, false, 0, false, true)]
    [InlineData(false, false, 80, true, true)]
    public void SoundMarks_OnWhenSoundIsOff(bool marks, bool mute, int master, bool muteArg, bool expected) =>
        Assert.Equal(expected, new GameSettings { SoundMarks = marks, Mute = mute, Master = master }.SoundMarksEffective(muteArg));

    [Fact]
    public void AttackPreview_OnByDefault_AndKept()
    {
        Assert.True(new GameSettings().PreviewAttacks);
        Assert.False(GameSettings.FromJson(new GameSettings { PreviewAttacks = false }.ToJson()).PreviewAttacks);
        Assert.True(GameSettings.FromJson("""{ "Master": 50 }""").PreviewAttacks);
    }
}
