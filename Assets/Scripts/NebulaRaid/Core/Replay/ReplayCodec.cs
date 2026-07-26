using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NebulaRaid.Combat;

namespace NebulaRaid.Replay
{
    /// <summary>
    /// Strict line-oriented replay codec. The format is intentionally simple so
    /// it can be inspected, diffed and parsed without a third-party serializer.
    /// </summary>
    public static class ReplayCodec
    {
        private const string Header = "NEBULA_RAID_REPLAY|1";

        public static string Serialize(ReplayData replay)
        {
            if (replay == null)
            {
                throw new ArgumentNullException(nameof(replay));
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine(Header);
            BattleDefinition definition = replay.Definition;
            builder.Append("BATTLE|")
                .Append(definition.TickRate.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(definition.Seed.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(definition.ArenaHalfExtentMm.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(definition.SpatialCellSizeMm.ToString(CultureInfo.InvariantCulture))
                .AppendLine();
            builder.Append("ACTORS|")
                .Append(definition.ActorCount.ToString(CultureInfo.InvariantCulture))
                .AppendLine();

            for (int i = 0; i < definition.ActorCount; i++)
            {
                ActorSpawnSpec actor = definition.GetActor(i);
                builder.Append("ACTOR|")
                    .Append(actor.Team.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(actor.PositionMm.X.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(actor.PositionMm.Y.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(actor.MaxHealth.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(actor.SpeedMmPerTick.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(actor.Damage.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(actor.AttackRangeMm.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(actor.AttackCooldownTicks.ToString(CultureInfo.InvariantCulture))
                    .AppendLine();
            }

            builder.Append("FRAMES|")
                .Append(replay.Frames.Length.ToString(CultureInfo.InvariantCulture))
                .AppendLine();
            for (int i = 0; i < replay.Frames.Length; i++)
            {
                ReplayFrame frame = replay.Frames[i];
                builder.Append("FRAME|")
                    .Append(frame.Tick.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(frame.PostStepChecksum.ToString("X16", CultureInfo.InvariantCulture)).Append('|')
                    .Append(frame.Commands.Length.ToString(CultureInfo.InvariantCulture))
                    .AppendLine();
                for (int commandIndex = 0; commandIndex < frame.Commands.Length; commandIndex++)
                {
                    InputCommand command = frame.Commands[commandIndex];
                    builder.Append("CMD|")
                        .Append(command.EntityId.ToString(CultureInfo.InvariantCulture)).Append('|')
                        .Append(command.MoveX.ToString(CultureInfo.InvariantCulture)).Append('|')
                        .Append(command.MoveY.ToString(CultureInfo.InvariantCulture)).Append('|')
                        .Append(command.AbilityMask.ToString(CultureInfo.InvariantCulture))
                        .AppendLine();
                }
            }

            builder.AppendLine("END");
            return builder.ToString();
        }

        public static ReplayData Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new FormatException("Replay is empty.");
            }

            string normalized = text.Replace("\r\n", "\n");
            string[] rawLines = normalized.Split('\n');
            List<string> lines = new List<string>(rawLines.Length);
            for (int i = 0; i < rawLines.Length; i++)
            {
                if (rawLines[i].Length > 0)
                {
                    lines.Add(rawLines[i]);
                }
            }

            int cursor = 0;
            ExpectExact(lines, ref cursor, Header);
            string[] battle = TakeParts(lines, ref cursor, "BATTLE", 5);
            int tickRate = ParseInt(battle[1], "tick rate");
            uint seed = ParseUInt(battle[2], "seed");
            int arena = ParseInt(battle[3], "arena");
            int cellSize = ParseInt(battle[4], "cell size");

            string[] actorCountLine = TakeParts(lines, ref cursor, "ACTORS", 2);
            int actorCount = ParseBoundedCount(actorCountLine[1], "actor count", 100_000);
            ActorSpawnSpec[] actors = new ActorSpawnSpec[actorCount];
            for (int i = 0; i < actorCount; i++)
            {
                string[] actor = TakeParts(lines, ref cursor, "ACTOR", 9);
                actors[i] = new ActorSpawnSpec(
                    ParseByte(actor[1], "team"),
                    new Int2(ParseInt(actor[2], "x"), ParseInt(actor[3], "y")),
                    ParseInt(actor[4], "health"),
                    ParseInt(actor[5], "speed"),
                    ParseInt(actor[6], "damage"),
                    ParseInt(actor[7], "range"),
                    ParseInt(actor[8], "cooldown"));
            }

            BattleDefinition definition = new BattleDefinition(
                tickRate,
                seed,
                arena,
                cellSize,
                actors);

            string[] frameCountLine = TakeParts(lines, ref cursor, "FRAMES", 2);
            int frameCount = ParseBoundedCount(frameCountLine[1], "frame count", 10_000_000);
            ReplayFrame[] frames = new ReplayFrame[frameCount];
            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                string[] frameLine = TakeParts(lines, ref cursor, "FRAME", 4);
                int tick = ParseInt(frameLine[1], "tick");
                if (!ulong.TryParse(
                    frameLine[2],
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out ulong checksum))
                {
                    throw new FormatException("Invalid checksum.");
                }

                int commandCount = ParseBoundedCount(
                    frameLine[3],
                    "command count",
                    definition.ActorCount);
                InputCommand[] commands = new InputCommand[commandCount];
                int previousEntity = -1;
                for (int commandIndex = 0; commandIndex < commandCount; commandIndex++)
                {
                    string[] commandLine = TakeParts(lines, ref cursor, "CMD", 5);
                    int entityId = ParseInt(commandLine[1], "entity id");
                    if (entityId <= previousEntity)
                    {
                        throw new FormatException("Commands are not in canonical entity order.");
                    }

                    previousEntity = entityId;
                    commands[commandIndex] = new InputCommand(
                        tick,
                        entityId,
                        ParseSByte(commandLine[2], "move x"),
                        ParseSByte(commandLine[3], "move y"),
                        ParseByte(commandLine[4], "ability mask"));
                }

                frames[frameIndex] = new ReplayFrame(tick, commands, checksum);
            }

            ExpectExact(lines, ref cursor, "END");
            if (cursor != lines.Count)
            {
                throw new FormatException("Unexpected data after replay end marker.");
            }

            return new ReplayData(definition, frames);
        }

        private static string[] TakeParts(
            List<string> lines,
            ref int cursor,
            string expectedTag,
            int expectedParts)
        {
            if (cursor >= lines.Count)
            {
                throw new FormatException("Unexpected end of replay.");
            }

            string[] parts = lines[cursor++].Split('|');
            if (parts.Length != expectedParts || parts[0] != expectedTag)
            {
                throw new FormatException("Expected " + expectedTag + " record.");
            }

            return parts;
        }

        private static void ExpectExact(List<string> lines, ref int cursor, string expected)
        {
            if (cursor >= lines.Count || lines[cursor++] != expected)
            {
                throw new FormatException("Expected '" + expected + "'.");
            }
        }

        private static int ParseBoundedCount(string value, string label, int maximum)
        {
            int parsed = ParseInt(value, label);
            if (parsed < 0 || parsed > maximum)
            {
                throw new FormatException(label + " is outside the supported range.");
            }

            return parsed;
        }

        private static int ParseInt(string value, string label)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                throw new FormatException("Invalid " + label + ".");
            }

            return parsed;
        }

        private static uint ParseUInt(string value, string label)
        {
            if (!uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed))
            {
                throw new FormatException("Invalid " + label + ".");
            }

            return parsed;
        }

        private static byte ParseByte(string value, string label)
        {
            if (!byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte parsed))
            {
                throw new FormatException("Invalid " + label + ".");
            }

            return parsed;
        }

        private static sbyte ParseSByte(string value, string label)
        {
            if (!sbyte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out sbyte parsed))
            {
                throw new FormatException("Invalid " + label + ".");
            }

            return parsed;
        }
    }
}

