using Spiegelwerk.Core.Records;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    public class HoleTests
    {
        // Numbers and names as in swWzdHoleTypes_e (SOLIDWORKS 2019 interop).
        [Theory]
        [InlineData(0, HoleTypes.Legacy)]                // swSimple
        [InlineData(9, HoleTypes.Legacy)]                // swCounterDrilledDrilled
        [InlineData(10, HoleTypes.CounterBore)]          // swCounterBoreBlind
        [InlineData(21, HoleTypes.CounterBore)]          // swCounterBoreThruCounterSinkTopMiddleBottom
        [InlineData(22, HoleTypes.Hole)]                 // swHoleBlind
        [InlineData(23, HoleTypes.Hole)]                 // swHoleBlindCounterSinkTop
        [InlineData(24, HoleTypes.CounterSink)]          // swCounterSinkBlind
        [InlineData(25, HoleTypes.Hole)]                 // swHoleThru
        [InlineData(28, HoleTypes.Hole)]                 // swHoleThruCounterSinkTopBottom
        [InlineData(29, HoleTypes.CounterSink)]          // swCounterSinkThru
        [InlineData(30, HoleTypes.CounterSink)]          // swCounterSinkThruCounterSinkBottom
        [InlineData(31, HoleTypes.Tap)]                  // swTapBlind
        [InlineData(36, HoleTypes.Tap)]                  // swTapThruCounterSinkTopBottom
        [InlineData(37, HoleTypes.PipeTap)]              // swPipeTapBlind
        [InlineData(42, HoleTypes.PipeTap)]              // swPipeTapThruCounterSinkTopBottom
        [InlineData(43, HoleTypes.CounterSink)]          // swCounterSinkBlindWithoutHeadClearance
        [InlineData(45, HoleTypes.CounterSink)]          // swCounterSinkThruCounterSinkBottomWithoutHeadClearance
        [InlineData(46, HoleTypes.Tap)]                  // swTapBlindCosmeticThread
        [InlineData(56, HoleTypes.Tap)]                  // swTapBlindRemoveThread
        [InlineData(57, HoleTypes.CounterBoreSlot)]      // swCounterBoreSlotBlind
        [InlineData(68, HoleTypes.CounterBoreSlot)]      // swCounterBoreSlotThruCounterSinkTopMiddleBottom
        [InlineData(69, HoleTypes.HoleSlot)]             // swSlotBlind
        [InlineData(71, HoleTypes.CounterSinkSlot)]      // swCounterSinkSlotBlind
        [InlineData(75, HoleTypes.HoleSlot)]             // swSlotThruCounterSinkTopBottom
        [InlineData(79, HoleTypes.CounterSinkSlot)]      // swCounterSinkSlotThruWithoutHeadClearance
        [InlineData(90, HoleTypes.CounterSinkSlot)]      // swCounterSinkSlotThruCounterSinkBottomWithoutHeadClearance
        public void Detailed_hole_type_maps_to_its_general_type(int holeType, int generic)
        {
            Assert.Equal(generic, HoleTypes.GenericOf(holeType));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(80)]
        [InlineData(91)]
        public void Unknown_hole_types_are_not_guessed(int holeType)
        {
            Assert.Null(HoleTypes.GenericOf(holeType));
        }
    }
}
