using BoscaliSummer.Features.Radio.Domain;

namespace BoscaliSummer.Tests.Features.Radio
{
    /// <summary>Pins the radio widget's copy and progress clamp.</summary>
    internal static class RadioHudCopyTests
    {
        public static void Run()
        {
            TestAssert.That(RadioHudCopy.Text("AGRAPOL FM") == "RADIO · AGRAPOL FM",
                "the channel name follows the owner");
            TestAssert.That(RadioHudCopy.Text(null) == "RADIO" && RadioHudCopy.Text("") == "RADIO",
                "an unnamed channel still reads as radio");

            TestAssert.That(RadioHudCopy.Detail("THE LONG WAY HOME", false) == "THE LONG WAY HOME",
                "a playing track reads by name");
            TestAssert.That(RadioHudCopy.Detail("THE LONG WAY HOME", true) == "PAUSED · THE LONG WAY HOME",
                "a paused track says so");
            TestAssert.That(RadioHudCopy.Detail(null, false) == "DEAD AIR" &&
                RadioHudCopy.Detail(null, true) == "PAUSED",
                "no title reports the deck state honestly");

            TestAssert.That(RadioHudCopy.Bar(0.5f) == 0.5f && RadioHudCopy.Bar(3f) == 1f &&
                RadioHudCopy.Bar(-3f) == 0f, "the bar is the clamped playback progress");
        }
    }
}
