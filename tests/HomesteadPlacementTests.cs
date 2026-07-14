using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class HomesteadPlacementTests
    {
        [Fact]
        public void Selector_is_stable_and_respects_per_type_targets_and_minimum_distance()
        {
            var candidates = new List<HomesteadCandidate>
            {
                C("WoodHouse1", 0, 0), C("WoodHouse1", 4, 0), C("WoodHouse1", 8, 0),
                C("WoodHouse2", 0, 4), C("WoodHouse2", 4, 4), C("WoodHouse2", 8, 4),
            };
            var config = new HomesteadSelectionConfig("Astley", "selector-v1", 3.0, 0.40);

            var first = HomesteadSelector.Select(candidates, config);
            var second = HomesteadSelector.Select(new List<HomesteadCandidate>(candidates), config);

            Assert.Equal(first.Selected, second.Selected);
            Assert.Equal(4, first.Selected.Count);
            Assert.Empty(first.Warnings);
            for (var i = 0; i < first.Selected.Count; i++)
                for (var j = i + 1; j < first.Selected.Count; j++)
                    Assert.True(first.Selected[i].DistanceSquaredTo(first.Selected[j]) >= 9.0);
        }

        [Fact]
        public void Selector_warns_instead_of_violating_proximity_when_target_is_impossible()
        {
            var candidates = new List<HomesteadCandidate>
            {
                new HomesteadCandidate("WoodHouse1", 0, 0, 0.0, 0.0, 10.0),
                new HomesteadCandidate("WoodHouse1", 1, 0, 1.0, 0.0, 10.0),
                new HomesteadCandidate("WoodHouse1", 2, 0, 2.0, 0.0, 10.0),
            };

            var result = HomesteadSelector.Select(
                candidates,
                new HomesteadSelectionConfig("Astley", "selector-v1", 128.0, 1.0));

            Assert.Single(result.Selected);
            Assert.Single(result.Warnings);
            Assert.Contains("WoodHouse1", result.Warnings[0]);
            Assert.Contains("1 of target 3", result.Warnings[0]);
        }

        [Fact]
        public void Seat_generation_is_stable_bounded_and_changes_by_attempt()
        {
            var candidate = new HomesteadCandidate("WoodHouse1", 56, -52, 100.0, 200.0, 10.0);

            var first = HomesteadSeatGenerator.Generate("Astley", "seat-v1", candidate, 8);
            var second = HomesteadSeatGenerator.Generate("Astley", "seat-v1", candidate, 8);

            Assert.Equal(first, second);
            Assert.Equal(8, first.Count);
            Assert.Equal(8, new HashSet<SeatCandidate>(first).Count);
            foreach (var seat in first)
            {
                var distance = Math.Sqrt(((seat.X - 100.0) * (seat.X - 100.0)) + ((seat.Z - 200.0) * (seat.Z - 200.0)));
                Assert.InRange(distance, 1.75, 9.2);
            }
        }

        [Fact]
        public void Seat_selection_returns_first_valid_attempt_or_an_honest_skip()
        {
            var seats = HomesteadSeatGenerator.Generate("Astley", "seat-v1", C("WoodHouse1", 0, 0), 8);

            var accepted = HomesteadSeatGenerator.Choose(seats, s => s.Attempt == 3);
            var skipped = HomesteadSeatGenerator.Choose(seats, _ => false);

            Assert.True(accepted.HasSeat);
            Assert.Equal(3, accepted.Seat.Attempt);
            Assert.Equal(4, accepted.AttemptsEvaluated);
            Assert.False(skipped.HasSeat);
            Assert.Equal(8, skipped.AttemptsEvaluated);
        }

        [Fact]
        public void World_identity_is_the_invariant_world_uid_only()
        {
            Assert.Equal("uid:2122292705", HomesteadWorldIdentity.FromUid(2122292705L));
            Assert.Equal("uid:-42", HomesteadWorldIdentity.FromUid(-42L));
        }

        [Fact]
        public void Selector_priority_uses_the_canonical_uid_version_prefab_zone_shape()
        {
            Assert.Equal(
                "11E14CB26C3EDDC97EAA55DC4D732BDA18ACAAF7C8FDC3A9022488E7FCAD712E",
                StableHash.Hex("uid:2122292705", "niflheim-homestead-playtest-v1", "WoodHouse5", "-25", "-30"));
        }

        [Fact]
        public void Seat_selection_scores_all_valid_attempts_and_chooses_best_clear_yard()
        {
            var seats = new List<SeatCandidate>
            {
                new SeatCandidate(0, 1.0, 0.0),
                new SeatCandidate(1, 5.0, 0.0),
                new SeatCandidate(2, 11.0, 0.0),
                new SeatCandidate(3, 5.0, 0.0),
            };
            var evaluations = new Dictionary<int, SeatEvaluation>
            {
                [0] = new SeatEvaluation(true, 0.5, 1.0, 4.0),
                [1] = new SeatEvaluation(true, 2.85, 5.0, 4.0),
                [2] = new SeatEvaluation(true, 4.0, 11.0, 4.0),
                [3] = new SeatEvaluation(true, 2.85, 5.0, 4.0),
            };

            var chosen = HomesteadSeatGenerator.ChooseBest(seats, seat => evaluations[seat.Attempt]);

            Assert.True(chosen.HasSeat);
            Assert.Equal(1, chosen.Seat.Attempt);
            Assert.Equal(4, chosen.AttemptsEvaluated);
        }

        [Fact]
        public void Seat_selection_skips_when_all_eight_attempts_are_invalid()
        {
            var seats = HomesteadSeatGenerator.Generate("uid:2122292705", "seat-v1", C("WoodHouse1", 0, 0), 8);

            var skipped = HomesteadSeatGenerator.ChooseBest(
                seats,
                _ => new SeatEvaluation(false, 100.0, 5.0, 4.0));

            Assert.False(skipped.HasSeat);
            Assert.Equal(8, skipped.AttemptsEvaluated);
        }

        [Fact]
        public void Host_structure_attribution_excludes_player_pieces_and_other_locations()
        {
            Assert.True(HomesteadHostStructure.IsAttributed(
                creator: 0L, pieceX: 104.0, pieceZ: 196.0, hostX: 100.0, hostZ: 200.0, locationRadius: 10.0));
            Assert.False(HomesteadHostStructure.IsAttributed(
                creator: 123L, pieceX: 104.0, pieceZ: 196.0, hostX: 100.0, hostZ: 200.0, locationRadius: 10.0));
            Assert.False(HomesteadHostStructure.IsAttributed(
                creator: 0L, pieceX: 111.0, pieceZ: 200.0, hostX: 100.0, hostZ: 200.0, locationRadius: 10.0));
        }

        [Fact]
        public void Assignment_metadata_requires_world_selector_prefab_and_zone_parity()
        {
            var expected = new HomesteadAssignmentMetadata(
                "uid:2122292705", "niflheim-homestead-playtest-v1", "WoodHouse5", -25, -30);

            Assert.True(expected.Matches(expected));
            Assert.False(expected.Matches(new HomesteadAssignmentMetadata(
                "uid:2122292705", "niflheim-homestead-playtest-v2", "WoodHouse5", -25, -30)));
            Assert.False(expected.Matches(new HomesteadAssignmentMetadata(
                "uid:2122292705", "niflheim-homestead-playtest-v1", "WoodHouse4", -25, -30)));
            Assert.False(expected.Matches(new HomesteadAssignmentMetadata(
                "uid:999", "niflheim-homestead-playtest-v1", "WoodHouse5", -25, -30)));
        }

        [Fact]
        public void Visual_motion_is_a_stable_four_second_loop()
        {
            var start = HomesteadVisualMotion.Sample(0.0);
            var quarter = HomesteadVisualMotion.Sample(1.0);
            var loop = HomesteadVisualMotion.Sample(4.0);

            Assert.Equal(0.0, start.HeightOffset, 6);
            Assert.Equal(start, loop);
            Assert.InRange(quarter.HeightOffset, 0.044, 0.046);
            Assert.InRange(quarter.YawDegrees, 1.49, 1.51);
        }

        private static HomesteadCandidate C(string prefab, int zoneX, int zoneZ) =>
            new HomesteadCandidate(prefab, zoneX, zoneZ, zoneX * 100.0, zoneZ * 100.0, 10.0);
    }
}
