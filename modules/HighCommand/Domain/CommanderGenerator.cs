using System.Text;

namespace BoscaliSummer.Features.HighCommand.Domain
{
    internal sealed class CommandPerson
    {
        public int Seed;
        public string Name;
        public string Rank;
        public CommandTrait Traits;
        public int Decorations;

        public CommandPerson(int seed, string name, string rank, CommandTrait traits)
        {
            Seed = seed; Name = name; Rank = rank; Traits = traits;
        }
    }

    /// <summary>
    /// Generated staff identities. The whole person is a pure function of (seed, tier), so
    /// the console can render a name, portrait and bio on any client from the synced seed.
    /// Text stays inside the MFD's row widths: name 24, rank 8, bio 280 characters.
    /// </summary>
    internal static class CommanderGenerator
    {
        public const int MaxName = 24;
        public const int MaxBio = 280;

        private static readonly string[] FirstNames =
        {
            "Aldo", "Mira", "Voss", "Karo", "Ilse", "Dane", "Rhea", "Osk", "Petra", "Cato",
            "Nadia", "Emil", "Saskia", "Tomas", "Vera", "Janus", "Leona", "Marek", "Iris", "Otto",
            "Freya", "Anton", "Zara", "Bruno", "Heidi", "Viktor", "Astrid", "Pavel", "Nora", "Rufus",
        };

        private static readonly string[] LastNames =
        {
            "Voss", "Keller", "Iri", "Marek", "Holt", "Brandt", "Sato", "Dorn", "Reyes", "Falk",
            "Ilyich", "Norr", "Crane", "Varga", "Osei", "Mercer", "Rask", "Kade", "Veil", "Straka",
            "Lindt", "Corvo", "Bahr", "Petrov", "Nakal", "Greve",
        };

        private static readonly string[] Academies =
        {
            "Kestrel", "Varyag", "Northreach", "Amara", "Selene", "Halden",
        };

        private static readonly string[] Places =
        {
            "Kessler Flats", "Port Amara", "Vetka", "Sable Coast", "Tern Valley", "Old Kray",
            "Ilyev", "Cormorant Sound", "Mount Rask", "Halden",
        };

        private static readonly string[] Events =
        {
            "Salt War", "Coastal Emergency", "Vetka Airlift", "Kray Incursion",
        };

        private static readonly string[] Postings =
        {
            "the 3rd Air Wing", "the 12th Rifle Corps", "Northern Training Command",
            "the 4th Fleet detachment", "the depots at Halden",
        };

        private static readonly string[] TraitLines =
        {
            "Keeps the supply ledger tighter than the operations map.",
            "Known across the ranks by first name; the staff would follow them into weather.",
            "Quotes doctrine at breakfast and means every word of it.",
            "Has the scars and the silence to go with them.",
            "A careful reader of people; every favor is entered in a private ledger.",
            "Rarely seen at the airfield; commands from a bunker no map marks.",
        };

        public static CommandPerson Create(int seed, int tier)
        {
            var stream = new SeedStream(unchecked((uint)seed * 2654435761u + 0x9E3779B9u));
            string name = stream.Pick(FirstNames) + " " + stream.Pick(LastNames);
            CommandTrait traits = RollTraits(stream);

            var person = new CommandPerson(seed, Trim(name, MaxName), CommandTier.Rank(tier), traits);
            return person;
        }

        private static CommandTrait RollTraits(SeedStream stream)
        {
            var first = (CommandTrait)(1 << stream.Range(6));
            CommandTrait mask = first;
            if (stream.Chance(40))
            {
                var second = (CommandTrait)(1 << stream.Range(6));
                if (second != first) mask |= second;
            }
            return mask;
        }

        public static string Bio(int seed, CommandTrait traits)
        {
            var stream = new SeedStream(unchecked((uint)seed ^ 0xA5A5A5A5u));
            string academy = stream.Pick(Academies);
            string place = stream.Pick(Places);
            string otherPlace = stream.Pick(Places);
            string incident = stream.Pick(Events);
            string posting = stream.Pick(Postings);

            var builder = new StringBuilder(MaxBio + 32);
            switch (stream.Range(3))
            {
                case 0:
                    builder.Append("Commissioned out of the ").Append(academy)
                           .Append(" officer school; first posting was ").Append(otherPlace).Append('.');
                    break;
                case 1:
                    builder.Append("Rose through the ").Append(place)
                           .Append(" territorial garrison after ").Append(12 + stream.Range(18))
                           .Append(" years in uniform.");
                    break;
                default:
                    builder.Append("A ").Append(place)
                           .Append(" native who moved to the general staff after the ")
                           .Append(incident).Append('.');
                    break;
            }

            builder.Append(' ').Append("Ran ").Append(posting).Append(" before this posting.");
            CommandTrait primary = PrimaryTrait(traits);
            if (primary != CommandTrait.None)
            {
                builder.Append(' ').Append(TraitLines[TraitIndex(primary)]);
            }

            return Trim(builder.ToString(), MaxBio);
        }

        private static CommandTrait PrimaryTrait(CommandTrait mask)
        {
            for (int bit = 0; bit < 6; bit++)
            {
                var trait = (CommandTrait)(1 << bit);
                if (CommandTraits.Has(mask, trait)) return trait;
            }
            return CommandTrait.None;
        }

        private static int TraitIndex(CommandTrait trait)
        {
            for (int bit = 0; bit < 6; bit++)
            {
                if ((CommandTrait)(1 << bit) == trait) return bit;
            }
            return 0;
        }

        private static string Trim(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string flattened = value.Replace('\n', ' ').Replace('\r', ' ');
            return flattened.Length <= max ? flattened : flattened.Substring(0, max);
        }
    }
}
