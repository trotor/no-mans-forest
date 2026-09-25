# No Man's Forest – 1 km × 1 km kartta oikeasta maastosta (vaihe 4a)

Päivämäärä: 2026-09-25
Tila: käyttäjän pyyntö ("isonna karttaa ja muuta sitä hiemaan, voit käyttää oikeaa karttaa mallina – hae jostain rajapinnasta pala karttaa, vaikka 1 km × 1 km"), toteutetaan suoraan
Liittyy: pääspeksi §9 (kartat), pikseligrafiikka `2026-09-24-pixel-art-design.md`

## 1. Tavoite

Uusi testitaistelukartta, jossa on:
- **koko:** 1000 × 1000 solua, eli 1 km × 1 km (1 solu = 1 m)
- **pohja:** oikea maasto Karhumäen eteläpuolelta Äänisjärven rannalta, 62,880° N, 34,440° E. Seutu oli Maaselän rintaman takamaastoa vuonna 1942.
- **muutokset:** kartta on muokattu vuoden 1942 henkeen
- **tekijänoikeudet:** avoimet tietolähteet ja niiden lisenssit on merkitty

Pelimoottorin täytyy pysyä sujuvana tämän kokoisella kartalla.

**Rajattu pois:**
- minikartta
- rakennukset
- juoksuhaudat karttaelementtinä
- useat kartat valikossa (kartan voi vaihtaa komentoriviltä `--map=`)

## 2. Tietolähteet

| Tieto | Lähde | Lisenssi / maininta |
|---|---|---|
| Maankäyttö, vedet, tiet, suot | OpenStreetMap, Overpass API | © OpenStreetMapin tekijät, ODbL 1.0 |
| Korkeus | ASTER GDEM v3 (NASA/METI), OpenTopoData API | vapaa käyttö maininnalla |

- **Välimuisti:** haettu raakadata tallennetaan versionhallintaan kansioon `tools/mapgen/data/karhumaki/` (`osm.json` ja `dem.json`). Kartta generoidaan siitä deterministisesti ilman verkkoyhteyttä.
- **Uusi haku:** `python3 -m tools.mapgen.fetch karhumaki` hakee datan uudelleen.
- **Lisenssi:** kartta on johdannainen OSM-tietokannasta, joten karttatiedosto on ODbL-lisenssin alainen. Maininta on README:ssä ja karttatiedoston ominaisuuksissa.

## 3. Muunnos pelikartaksi

**Projektio:** paikallinen tasoprojektio ruudun keskipisteestä. x kasvaa itään ja y etelään, ja 1 solu on 1 m.

**Maasto** (prioriteetti ylhäältä alas):

| OSM | Pelimaasto |
|---|---|
| `natural=water` (järvi, lampi) | `water`: uusi maasto, kulkukelvoton ja näkyvyyttä ei estä |
| `waterway=stream/ditch` | `swamp`, 3 m leveä (märkä puronvarsi) |
| `natural=wetland` | `swamp` |
| `highway=*` (tie) | `road`: tertiary 5 m, track 3 m, path 1,5 m |
| `natural=scrub`, `landuse=meadow/farmland/grass`, `natural=grassland` | `grass` + pensaita |
| `natural=wood`, `landuse=forest` ja merkitsemätön maa | `forest` |

**Äänisjärvi:** järven rantaviiva piirretään esteeksi, ja vesi täytetään koillisnurkasta (kulmasolu on järveä).

**Korkeus:**
- ASTER-verkko (33 × 33 pistettä, noin 31 m välein) interpoloidaan bikuubisesti soluihin.
- Korkeus mitataan matalimmasta kohdasta, eli järvenpinta on 0.
- Pikkukumpareiksi lisätään kohinaa ±40 cm.
- Järven korkeus on 0.
- **Uusi korkeustiilisarja:** `heights_fine.tsx`, jossa on 512 tiiltä 25 cm välein (0–127,75 m).

**Muutokset vuoden 1942 henkeen** (kiinteällä siemenellä):
- **Tiet:** nykyinen maantie on kapea kärrytie (4 m), metsäautotie on polku (2 m), ja rakennukset jätetään pois.
- **Metsä:** kohinalla tehdään harvennuksia, eli pieniä aukeita (noin 10 % metsästä).
- **Kivet:** siirtolohkareita metsään ja rinteisiin (noin 1 per 600 m²), jyrkimmille rinteille tiheämmin.
- **Pensaat:** metsänreunoihin, pensaikkoon ja suon reunoille.
- **Pelto:** tien varteen raivataan vanha pelto (noin 80 × 60 m), jotta siellä on avointa maastoa.

**Joukot:**
- **Suomalaiset:** 4 miestä lounaan metsässä.
- **Neuvostoliittolaiset:** 5 miestä pohjoisen kukkulan rinteessä, josta näkyy suolle ja tielle.
- **Partio:** kulkee kukkulan juurella.
- **Etäisyys:** suomalaisten lähtöpisteestä neuvostoasemiin on noin 350 m.

**Kartan ominaisuudet:** `source` (lähteet ja lisenssit) sekä `origin_lat` ja `origin_lon`.

## 4. Pelimoottori isolla kartalla

**Tavoite:** 1000 × 1000 solun kartalla
- 20 askelta sekunnissa ×4-nopeudella ilman nykimistä
- kartan lataus alle 5 s

| Kohta | Nyt | Muutos |
|---|---|---|
| A* | jokainen kutsu varaa ja nollaa 3 × 1 M -taulukot | uudelleenkäytettävät puskurit ja sukupolvileima, ei nollausta |
| Sumu (Godot) | 1 M `SetPixel`-kutsua päivitystä kohti | tavutaulukko ja `Image.SetData` |
| Maasto (Godot) | 2 M `SetPixel`-kutsua latauksessa | tavutaulukko; korkeusskaala shaderille kartan mukaan |
| Puut, kivet ja pensaat (Godot) | 2 Sprite2D-solmua kutakin kohden (noin 150 000 solmua) | 32 × 32 solun lohkot, jotka piirtävät omat koristeensa; latvusten häivytys piirretään uudelleen vain sotilaiden lähellä |
| TMX | vain CSV | lisäksi Tiledin `base64` + `zlib`/`gzip` |

## 5. Pelaaminen

- **Oletuskartta:** peli avautuu uudelle kartalle (`karhumaki.tmx`).
- **Vanha kartta:** `--map=skirmish` avaa vanhan kartan.
- **Testit:** vanha kartta jää testien vertailukartaksi.
- **Kamera:** avautuu suomalaisten kohdalle. Kauimmainen zoomaus riittää näyttämään suurimman osan kartasta.

## 6. Testaus

- **Python** (`tools/mapgen/tests`):
  - projektio
  - maaston prioriteetit
  - järven täyttö
  - korkeuden interpolointi ja nollataso
  - tiilisarjan korkeudet
  - deterministisyys
  - lähtöpisteet ovat kuljettavia
  - TMX:n rakenne
- **C#:**
  - base64/zlib/gzip-TMX
  - A*: sama tulos kuin ennen ja puskurit käytetään uudelleen
  - `water`-maasto on kulkukelvoton ja näkyvyydeltään avoin
- **Oikea kartta:**
  - lataus, koko ja lähtöpisteet
  - suomalaiset pääsevät neuvostoasemiin
  - kahden minuutin taistelu on deterministinen ja pysyy aikarajassa
- **Godot:** käännös, savutesti ja kuvakaappaus.
