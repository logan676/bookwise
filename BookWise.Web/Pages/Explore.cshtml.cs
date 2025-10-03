using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BookWise.Web.Pages
{
    public class ExploreModel : PageModel
    {
        public IReadOnlyList<AuthorProfile> Authors { get; private set; } = new List<AuthorProfile>();

        public void OnGet()
        {
            ViewData["Title"] = "Explore";
            Authors = BuildAuthors();
        }

        private static IReadOnlyList<AuthorProfile> BuildAuthors()
        {
            return new List<AuthorProfile>
            {
                new()
                {
                    Slug = "jrr-tolkien",
                    Name = "J.R.R. Tolkien",
                    Summary = "Creator of Middle-earth and author of The Lord of the Rings saga.",
                    PhotoUrl = "https://i.pravatar.cc/96?img=54",
                    WorkCount = 12,
                    Library = new List<AuthorWork>
                    {
                        new() { Title = "The Hobbit", Subtitle = "Middle-earth", CoverUrl = CoverById(14627509) },
                        new() { Title = "The Fellowship of the Ring", Subtitle = "The Lord of the Rings", CoverUrl = CoverById(14627060) },
                        new() { Title = "The Two Towers", Subtitle = "The Lord of the Rings", CoverUrl = CoverById(14627564) },
                        new() { Title = "The Return of the King", Subtitle = "The Lord of the Rings", CoverUrl = CoverById(14627062) },
                    },
                    AvailableWorks = new List<AuthorWork>
                    {
                        new() { Title = "The Silmarillion", Subtitle = "Legendarium", CoverUrl = CoverById(14627042) },
                        new() { Title = "Unfinished Tales", Subtitle = "Middle-earth", CoverUrl = CoverById(9293458) },
                        new() { Title = "The Children of Húrin", Subtitle = "Legendarium", CoverUrl = CoverById(8220298) },
                        new() { Title = "Beren and Lúthien", Subtitle = "Legendarium", CoverUrl = CoverById(12639912) },
                        new() { Title = "The Fall of Gondolin", Subtitle = "Legendarium", CoverUrl = CoverById(12451486) },
                        new() { Title = "Roverandom", Subtitle = "Fantasy", CoverUrl = CoverById(9293461) },
                    }
                },
                new()
                {
                    Slug = "george-rr-martin",
                    Name = "George R.R. Martin",
                    Summary = "Epic fantasist behind A Song of Ice and Fire.",
                    PhotoUrl = "https://i.pravatar.cc/96?img=12",
                    WorkCount = 8,
                    Library = new List<AuthorWork>
                    {
                        new() { Title = "A Game of Thrones", Subtitle = "A Song of Ice and Fire", CoverUrl = CoverById(4855) },
                        new() { Title = "A Clash of Kings", Subtitle = "A Song of Ice and Fire", CoverUrl = CoverById(7323439) },
                        new() { Title = "A Storm of Swords", Subtitle = "A Song of Ice and Fire", CoverUrl = CoverById(12538785) },
                        new() { Title = "A Feast for Crows", Subtitle = "A Song of Ice and Fire", CoverUrl = CoverById(6399778) },
                    },
                    AvailableWorks = new List<AuthorWork>
                    {
                        new() { Title = "A Dance with Dragons", Subtitle = "A Song of Ice and Fire", CoverUrl = CoverById(11299703) },
                        new() { Title = "Fire & Blood", Subtitle = "Targaryen History", CoverUrl = CoverById(15110788) },
                        new() { Title = "The Hedge Knight", Subtitle = "Tales of Dunk and Egg", CoverUrl = CoverById(15125419) },
                        new() { Title = "The Sworn Sword", Subtitle = "Tales of Dunk and Egg", CoverUrl = CoverById(15125419) },
                    }
                },
                new()
                {
                    Slug = "isaac-asimov",
                    Name = "Isaac Asimov",
                    Summary = "Prolific science fiction author and futurist.",
                    PhotoUrl = "https://i.pravatar.cc/96?img=33",
                    WorkCount = 15,
                    Library = new List<AuthorWork>
                    {
                        new() { Title = "Foundation", Subtitle = "Foundation Series", CoverUrl = CoverById(14612610) },
                        new() { Title = "Foundation and Empire", Subtitle = "Foundation Series", CoverUrl = CoverById(9300695) },
                        new() { Title = "Second Foundation", Subtitle = "Foundation Series", CoverUrl = CoverById(9261324) },
                        new() { Title = "I, Robot", Subtitle = "Robot Series", CoverUrl = CoverById(12385229) },
                    },
                    AvailableWorks = new List<AuthorWork>
                    {
                        new() { Title = "The Caves of Steel", Subtitle = "Robot Series", CoverUrl = CoverById(13790511) },
                        new() { Title = "The Naked Sun", Subtitle = "Robot Series", CoverUrl = CoverById(6542967) },
                        new() { Title = "The Robots of Dawn", Subtitle = "Robot Series", CoverUrl = CoverById(14372309) },
                        new() { Title = "The End of Eternity", Subtitle = "Standalone", CoverUrl = CoverById(6622699) },
                    }
                },
                new()
                {
                    Slug = "frank-herbert",
                    Name = "Frank Herbert",
                    Summary = "Science fiction visionary best known for Dune.",
                    PhotoUrl = "https://i.pravatar.cc/96?img=29",
                    WorkCount = 10,
                    Library = new List<AuthorWork>
                    {
                        new() { Title = "Dune", Subtitle = "The Atreides Saga", CoverUrl = CoverById(11481354) },
                        new() { Title = "Dune Messiah", Subtitle = "The Atreides Saga", CoverUrl = CoverById(2421405) },
                        new() { Title = "Children of Dune", Subtitle = "The Atreides Saga", CoverUrl = CoverById(6976407) },
                        new() { Title = "God Emperor of Dune", Subtitle = "The Atreides Saga", CoverUrl = CoverById(6711531) },
                    },
                    AvailableWorks = new List<AuthorWork>
                    {
                        new() { Title = "Heretics of Dune", Subtitle = "The Atreides Saga", CoverUrl = CoverById(284530) },
                        new() { Title = "Chapterhouse: Dune", Subtitle = "The Atreides Saga", CoverUrl = CoverById(5536140) },
                        new() { Title = "Dune: House Atreides", Subtitle = "Prelude", CoverUrl = CoverById(372913) },
                        new() { Title = "Destination: Void", Subtitle = "Standalone", CoverUrl = CoverById(10292985) },
                    }
                },
                new()
                {
                    Slug = "ursula-le-guin",
                    Name = "Ursula K. Le Guin",
                    Summary = "Award-winning author exploring society, gender, and imagination.",
                    PhotoUrl = "https://i.pravatar.cc/96?img=15",
                    WorkCount = 7,
                    Library = new List<AuthorWork>
                    {
                        new() { Title = "A Wizard of Earthsea", Subtitle = "Earthsea Cycle", CoverUrl = CoverById(13617691) },
                        new() { Title = "The Tombs of Atuan", Subtitle = "Earthsea Cycle", CoverUrl = CoverById(6633403) },
                        new() { Title = "The Farthest Shore", Subtitle = "Earthsea Cycle", CoverUrl = CoverById(6498990) },
                        new() { Title = "Tehanu", Subtitle = "Earthsea Cycle", CoverUrl = CoverById(3347790) },
                    },
                    AvailableWorks = new List<AuthorWork>
                    {
                        new() { Title = "The Left Hand of Darkness", Subtitle = "Hainish Cycle", CoverUrl = CoverById(10618463) },
                        new() { Title = "The Dispossessed", Subtitle = "Hainish Cycle", CoverUrl = CoverById(6979680) },
                        new() { Title = "Tales from Earthsea", Subtitle = "Earthsea Cycle", CoverUrl = CoverById(4636848) },
                        new() { Title = "The Lathe of Heaven", Subtitle = "Speculative", CoverUrl = CoverById(26458) },
                    }
                },
                new()
                {
                    Slug = "philip-k-dick",
                    Name = "Philip K. Dick",
                    Summary = "Speculative fiction pioneer questioning reality and identity.",
                    PhotoUrl = "https://i.pravatar.cc/96?img=7",
                    WorkCount = 11,
                    Library = new List<AuthorWork>
                    {
                        new() { Title = "Do Androids Dream of Electric Sheep?", Subtitle = "Standalone", CoverUrl = CoverById(207515) },
                        new() { Title = "The Man in the High Castle", Subtitle = "Standalone", CoverUrl = CoverById(420452) },
                        new() { Title = "Ubik", Subtitle = "Standalone", CoverUrl = CoverById(5018327) },
                        new() { Title = "A Scanner Darkly", Subtitle = "Standalone", CoverUrl = CoverById(911131) },
                    },
                    AvailableWorks = new List<AuthorWork>
                    {
                        new() { Title = "Flow My Tears, the Policeman Said", Subtitle = "Standalone", CoverUrl = CoverById(9251771) },
                        new() { Title = "VALIS", Subtitle = "VALIS Trilogy", CoverUrl = CoverById(9251944) },
                        new() { Title = "The Three Stigmata of Palmer Eldritch", Subtitle = "Standalone", CoverUrl = CoverById(4910773) },
                        new() { Title = "Minority Report", Subtitle = "Short Fiction", CoverUrl = CoverById(499534) },
                    }
                }
            };
        }

        private static string CoverById(int coverId) => $"https://covers.openlibrary.org/b/id/{coverId}-L.jpg";

        public class AuthorProfile
        {
            public string Slug { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string Summary { get; init; } = string.Empty;
            public string PhotoUrl { get; init; } = string.Empty;
            public int WorkCount { get; init; }
            public IReadOnlyList<AuthorWork> Library { get; init; } = new List<AuthorWork>();
            public IReadOnlyList<AuthorWork> AvailableWorks { get; init; } = new List<AuthorWork>();
        }

        public class AuthorWork
        {
            public string Title { get; init; } = string.Empty;
            public string Subtitle { get; init; } = string.Empty;
            public string CoverUrl { get; init; } = string.Empty;
        }
    }
}
