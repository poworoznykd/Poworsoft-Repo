using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CollectIQ.Enums
{
    public static class Enums
    {
        public enum CollectingCardCategory
        {
            Unknown = 0,

            // Sports
            Baseball,
            Basketball,
            Football,
            Hockey,
            Soccer,
            Golf,
            Tennis,
            Boxing,
            MMA,
            Wrestling,
            Cricket,
            Rugby,
            Lacrosse,
            Volleyball,
            Motorsport,     // F1/NASCAR/MotoGP/etc.
            Olympics,       // general Olympic / multi-sport sets
            Esports,

            // Non-sport / TCG
            Pokemon,
            YuGiOh,
            MagicTheGathering,
            Digimon,
            OnePiece,
            DragonBall,
            Lorcana,
            FleshAndBlood,
            WeissSchwarz,
            Metazoo,

            // Non-sport / Entertainment & brands
            Marvel,
            DC,
            StarWars,
            StarTrek,
            Disney,
            Pixar,
            Movies,
            TV,
            Anime,
            Manga,
            Comics,
            VideoGames,
            Music,
            Celebrities,

            // Other / misc
            Historical,
            SciFi,
            Fantasy,
            Horror,
            Other
        }
    }
}
