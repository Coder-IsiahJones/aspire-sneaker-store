using SneakerStore.Api.Models;

namespace SneakerStore.Api.Data;

public static class SeedData
{
    // Returns fresh instances each call because InsertMany writes the generated Id back onto them.
    public static List<Sneaker> Sneakers() =>
    [
        new()
        {
            Sku = "JRD-AJ1-CHI",
            Name = "Air Jordan 1 Retro High OG",
            Brand = "Jordan",
            Colorway = "Chicago",
            Price = 180m,
            ReleaseYear = 2015,
            Sizes = [8, 9, 9.5, 10, 11, 12],
            Colors = ["#F4F4F4", "#C8102E", "#F4F4F4"],
            Description = "The red, white, and black high top that started it all, cut from tumbled leather."
        },
        new()
        {
            Sku = "JRD-AJ4-BRD",
            Name = "Air Jordan 4 Retro",
            Brand = "Jordan",
            Colorway = "Bred",
            Price = 210m,
            ReleaseYear = 2019,
            Sizes = [8.5, 9, 10, 10.5, 11],
            Colors = ["#1A1A1A", "#C8102E", "#F4F4F4"],
            Description = "Black nubuck, cement grey wings, and a hit of red on the midsole."
        },
        new()
        {
            Sku = "NKE-AM1-ROY",
            Name = "Air Max 1",
            Brand = "Nike",
            Colorway = "Anniversary Royal",
            Price = 150m,
            ReleaseYear = 2017,
            Sizes = [7, 8, 9, 10, 11, 13],
            Colors = ["#F4F4F4", "#1E4FA3", "#F4F4F4"],
            Description = "Visible Air in the heel and royal blue mudguards on a white mesh base."
        },
        new()
        {
            Sku = "NKE-DNK-PND",
            Name = "Dunk Low",
            Brand = "Nike",
            Colorway = "Panda",
            Price = 115m,
            ReleaseYear = 2021,
            Sizes = [6, 7, 8, 9, 10, 11, 12],
            Colors = ["#F4F4F4", "#111111", "#F4F4F4"],
            Description = "Black overlays on a white leather base. Goes with everything."
        },
        new()
        {
            Sku = "NKE-AF1-WHT",
            Name = "Air Force 1 Low",
            Brand = "Nike",
            Colorway = "Triple White",
            Price = 115m,
            ReleaseYear = 2007,
            Sizes = [7, 8, 8.5, 9, 10, 11, 12, 13],
            Colors = ["#F7F7F5", "#E3E3E0", "#F7F7F5"],
            Description = "All white leather. Keep a pair of wipes handy."
        },
        new()
        {
            Sku = "ADI-SMB-OG",
            Name = "Samba OG",
            Brand = "adidas",
            Colorway = "Cloud White / Core Black",
            Price = 100m,
            ReleaseYear = 2023,
            Sizes = [6, 7, 8, 9, 10],
            Colors = ["#F5F3EE", "#111111", "#B5835A"],
            Description = "A soccer trainer from the 1950s with a suede T-toe and a gum sole."
        },
        new()
        {
            Sku = "ADI-GZL-GRN",
            Name = "Gazelle",
            Brand = "adidas",
            Colorway = "Collegiate Green",
            Price = 100m,
            ReleaseYear = 2017,
            Sizes = [8, 9, 10, 11],
            Colors = ["#0B5D3B", "#F4F4F4", "#F4F4F4"],
            Description = "Green suede, white stripes, gold foil lettering."
        },
        new()
        {
            Sku = "NB-990V6-GRY",
            Name = "990v6",
            Brand = "New Balance",
            Colorway = "Grey",
            Price = 200m,
            ReleaseYear = 2022,
            Sizes = [8, 9, 9.5, 10, 10.5, 11, 12],
            Colors = ["#B8BCBF", "#8D9093", "#E9E9E6"],
            Description = "Pigskin suede and mesh in the grey New Balance is known for."
        },
        new()
        {
            Sku = "NB-550-WGR",
            Name = "550",
            Brand = "New Balance",
            Colorway = "White Green",
            Price = 110m,
            ReleaseYear = 2021,
            Sizes = [7, 8, 9, 10, 11],
            Colors = ["#F4F4F4", "#1F5C3A", "#F4F4F4"],
            Description = "A 1989 basketball shoe that came back as an everyday low top."
        },
        new()
        {
            Sku = "ASC-GK14-CRM",
            Name = "GEL-Kayano 14",
            Brand = "ASICS",
            Colorway = "Cream / Black",
            Price = 150m,
            ReleaseYear = 2023,
            Sizes = [7, 8, 9, 10, 11, 12],
            Colors = ["#EDE6D6", "#111111", "#EDE6D6"],
            Description = "Late 2000s runner styling with layered overlays and GEL cushioning."
        },
        new()
        {
            Sku = "CNV-C70-PCH",
            Name = "Chuck 70 Hi",
            Brand = "Converse",
            Colorway = "Parchment",
            Price = 85m,
            ReleaseYear = 2018,
            Sizes = [6, 7, 8, 9, 10, 11, 12],
            Colors = ["#EDE3CF", "#111111", "#F2EBDD"],
            Description = "Heavier canvas, a glossy egret midsole, and the vintage heel patch."
        },
        new()
        {
            Sku = "VNS-OSK-BLK",
            Name = "Old Skool",
            Brand = "Vans",
            Colorway = "Black / White",
            Price = 70m,
            ReleaseYear = 2019,
            Sizes = [6, 7, 8, 9, 10, 11, 12, 13],
            Colors = ["#111111", "#F4F4F4", "#F4F4F4"],
            Description = "Suede and canvas with the side stripe. A skate shop staple."
        }
    ];
}
