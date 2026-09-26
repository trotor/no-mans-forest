using Nmf.Client;

namespace Nmf.Client.Tests;

public class FollowLeashTests
{
    // One axis: the view is 1000 px wide around its centre (half 500); the men must stay 150 px inside its edges.

    [Fact]
    public void MenWellInsideTheView_LeaveTheCameraWhereThePlayerPutIt()
    {
        Assert.Equal(1000f, FollowLeash.Centre(1000f, 500f, 800f, 1100f, 150f));
    }

    [Fact]
    public void MenWalkingOutOfTheInnerArea_DrawTheCameraAfterThem()
    {
        Assert.Equal(1250f, FollowLeash.Centre(1000f, 500f, 1400f, 1600f, 150f)); // their far edge 150 px inside the view's
        Assert.Equal(750f, FollowLeash.Centre(1000f, 500f, 400f, 600f, 150f));
    }

    [Fact]
    public void PanningFarAway_IsHeldBackSoTheMenStayInView()
    {
        // the player pans to 2000: the men at 800..1100 would be off the screen; the camera is held at 1150, their near
        // edge 150 px inside the view's
        Assert.Equal(1150f, FollowLeash.Centre(2000f, 500f, 800f, 1100f, 150f));
    }

    [Fact]
    public void MenSpreadWiderThanTheInnerArea_CanBeLookedAlong_ButNotLeft()
    {
        Assert.Equal(1000f, FollowLeash.Centre(1000f, 500f, 500f, 1500f, 150f)); // over the middle of the line
        Assert.Equal(1100f, FollowLeash.Centre(1100f, 500f, 500f, 1500f, 150f)); // over its eastern flank
        Assert.Equal(850f, FollowLeash.Centre(300f, 500f, 500f, 1500f, 150f));   // no farther than their western edge allows
    }

    [Fact]
    public void TheToggle_SaysWhatItDoes()
    {
        Assert.Equal("Seuraa: päällä (L)", FollowLeash.ButtonText(true, "fi"));
        Assert.Equal("Follow: off (L)", FollowLeash.ButtonText(false, "en"));
    }
}
