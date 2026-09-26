using Nmf.Client;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

public class HudTextTests
{
    [Fact]
    public void TheStatusLine_SaysWhatIsKnown_InTheChosenLanguage()
    {
        var map = new GridMap(60, 40, ["none"], new MapFeatures([],
            [new MapPoint("b1", "blue", new Vec2(1050, 1050)), new MapPoint("b2", "blue", new Vec2(1450, 1050)), new MapPoint("r1", "red", new Vec2(1850, 1450))], []));
        var session = new GameSession(SkirmishScenario.Create(map, 1));
        for (int i = 0; i < 30; i++)
            session.StepOnce();
        session.Sim.Units[1].Wound = WoundLevel.Light;
        string en = HudText.Status(session, "en", "platoon");
        Assert.Equal("Enemy: 1 seen · 0 heard · 0 last known      Losses: 0 killed · 1 wounded      Enemy down (seen): 0      Commanding: platoon      F1 help", en);
        string fi = HudText.Status(session, "fi", "joukkue");
        Assert.Equal("Vihollinen: 1 nähty · 0 kuultu · 0 muistissa      Tappiot: 0 kaatunutta · 1 haavoittunut      Vihollisia maassa (nähty): 0      Komennossa: joukkue      F1 ohje", fi);
    }
}
