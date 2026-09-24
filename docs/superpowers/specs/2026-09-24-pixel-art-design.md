# No Man's Forest – pikseligrafiikka ja luettava käyttöliittymä

Päivämäärä: 2026-09-24
Tila: hyväksytty keskustelussa, toteutetaan suoraan (käyttäjän pyyntö)
Liittyy: `2026-09-24-no-mans-forest-design.md` (pääspeksi, grafiikka §1.2 ja §14)

## 1. Tavoite

Testitaistelu näyttää JA2-aikakauden (1990-luvun loppu) pikselipeliltä ylhäältä päin kuvattuna, ja sitä voi pelata MacBookin Retina-näytöllä ilman siristelyä.

Tilanne nyt:
- Grafiikka on yksi väri per solu, ja sotilaat ovat palloja.
- Ikkuna on 1280 × 800 fyysistä pikseliä, mikä Retinalla näkyy puolen kokoisena, ja teksti on 15 px.

Onnistumisen mittarit:
- Kuvakaappauksessa maasto näyttää tekstuuriselta ja orgaaniselta (ei ruudukkoa).
- Puilla on latvukset ja varjot.
- Sotilaat ovat tunnistettavia ihmishahmoja, jotka kääntyvät kulkusuuntaan ja joiden jalat liikkuvat askeltahdissa.
- Tekstit ovat luettavia läppärin ruudulla normaalilta katseluetäisyydeltä.

## 2. Linjaukset

**Näkökulma ja mittakaava**
- **Näkökulma:** puhtaasti ylhäältä päin (ei isometriaa).
- **Valo:** tulee luoteesta, ja varjot lankeavat kaakkoon.
- **Mittakaava:** 32 pikseliä per metri (per solu).
- **Sotilaan koko:** hartiat noin 20 px, makuulla noin 58 px pitkä.

**Tyyli**
- **Paletti:** rajattu, maanläheinen, noin 48 väriä.
- **Yhtenäisyys:** sprite-hahmot kvantisoidaan palettiin, ja niissä on 1 px tumma ääriviiva.
- **Rasterointi:** tekstuureissa käytetään järjestettyä rasterointia (Bayer 4×4).

**Grafiikan lähde**
- Grafiikka generoidaan Python-skriptillä (`tools/art`, Pillow ja numpy) deterministisesti kiinteistä siemenistä.
- Generoidut PNG- ja JSON-tiedostot tallennetaan versionhallintaan kansioon `content/core/art/`, joten pelaaja ei tarvitse Pythonia.
- Kuvat voi korvata käsin piirretyillä tai toisen tekoälyn tekemillä tiedostoilla, kunhan ne noudattavat kohdan 5 formaattia.

**Lisenssi:** CC BY-SA 4.0, kuten muullakin grafiikalla.

## 3. Maasto

- **Tekstuurit:** jokaiselle maastolle on oma saumaton 128 × 128 px tekstuuri (4 × 4 m): ruoho, metsänpohja, suo ja tie. Tuntematon maasto piirretään ruohona.
- **Yhdistäminen:** Godot-shader valitsee jokaiselle pikselille maaston solukartasta. Hakukohtaa siirretään kohinalla (noin ±0,45 solua), jolloin maastojen rajat ovat rosoisia ja orgaanisia.
- **Kukkulat:** korkeuskartasta lasketaan varjostus valolla luoteesta, joten kukkulat erottuvat.
- **Esineet** piirretään spriteinä maaston päälle:
  - **kivet:** solut, joissa on este ja joiden läpi ei pääse
  - **pensaat:** kuljettavat solut, joissa on alle 150 cm korkea este
  - **puut:** kuusia (70 %) ja koivuja (30 %) metsäsoluissa, noin yksi puu 3 × 3 metrin lohkoa kohden. Paikka, laji ja muunnelma valitaan hajautusfunktiolla deterministisesti.
- **Varjot:** puilla ja kivillä on pehmeä pudotusvarjo kaakkoon.
- **Latvukset:**
  - Ne piirretään sotilaiden päälle 80 %:n peittävyydellä.
  - Jos oma sotilas on 2,5 metrin säteellä, peittävyys laskee 35 %:iin, jotta metsässä liikkuvat miehet näkyvät.
  - Näkyvät viholliset näkyvät latvusten läpi samalla tavalla.

## 4. Sotilaat

**Asut**
- **Suomalainen:** harmaanvihreä M36-asu ja M40-kypärä.
- **Neuvostosotilas:** khaki gimnastjorka ja vihreä SSh-40-kypärä.
- **Molemmilla** on kivääri, leipälaukku ja saappaat.

**Piirtotapa**
- Hahmo mallinnetaan vektoriosina metreissä: jalat, vartalo, kädet, kivääri, laukku ja kypärä.
- Hahmo kierretään suuntaan ja piirretään neljä kertaa suurempana.
- Sen jälkeen kuva pienennetään, alfa kynnystetään kahteen tasoon, värit kvantisoidaan palettiin ja lisätään ääriviiva.
- Valon puoli (luode) on sama jokaisessa suunnassa, joten valaistus pysyy yhtenäisenä.

**Suunnat:** 8 kappaletta. Simulaatiossa ei vielä ole suuntaa, joten asiakaspuoli päättelee sen liikkeestä ja muistaa viimeisen.

**Animaatiot**

| Nimi | Kuvia | Käyttö |
|---|---|---|
| idle | 1 | seisoo paikallaan |
| walk | 6 | kävelee |
| run | 6 | juoksee |
| crouch | 1 | kyykyssä |
| prone | 1 | makaa |
| crawl | 4 | ryömii |

**Askeltahti:** animaation kuva valitaan kuljetusta matkasta. Kävelyn askelsykli on 120 cm, juoksun 180 cm ja ryöminnän 60 cm, joten jalat eivät liu'u.

**Muotokuvat:** 64 × 64 px JA2-henkinen rintakuva kypärän kanssa, 8 muunnelmaa kummallekin puolelle.

## 5. Tiedostoformaatit (sopimus myös ulkopuolisille kuville)

`content/core/art/soldiers/<faction>.png`, missä `<faction>` on `finnish` tai `soviet`:
- **Ruudukko:** jokainen ruutu on 64 × 64 px, ja tausta on läpinäkyvä.
- **Keskipiste:** yksikön sijainti on ruudun keskellä (32, 32).
- **Suunta:** hahmon etusuunta on ruudun ylälaita, kun suunta on N.
- **Rivit:** jokaisella animaatiolla on 8 riviä, yksi kutakin suuntaa kohden järjestyksessä N, NE, E, SE, S, SW, W ja NW. Animaatiot ovat peräkkäin järjestyksessä idle, walk, run, crouch, prone, crawl ja valinnainen dead. Kaikkiaan rivejä on 56.
- **Sarakkeet:** animaation kuvat ovat vasemmalta oikealle, enintään 6.
- **Koko:** 384 × 3584 px (ilman dead-riviä riittää 384 × 3072).
- **Metatiedot:** `content/core/art/soldiers/sheet.json`

```json
{
  "cellSize": 64,
  "pixelsPerMetre": 32,
  "directions": ["N", "NE", "E", "SE", "S", "SW", "W", "NW"],
  "animations": {
    "idle":   { "row": 0,  "frames": 1, "strideCm": 0 },
    "walk":   { "row": 8,  "frames": 6, "strideCm": 120 },
    "run":    { "row": 16, "frames": 6, "strideCm": 180 },
    "crouch": { "row": 24, "frames": 1, "strideCm": 0 },
    "prone":  { "row": 32, "frames": 1, "strideCm": 0 },
    "crawl":  { "row": 40, "frames": 4, "strideCm": 60 },
    "dead":   { "row": 48, "frames": 1, "strideCm": 0 }
  }
}
```

**Muut tiedostot**
- **Muotokuvat:** `content/core/art/portraits/<faction>.png`, 8 kappaletta 64 × 64 px vierekkäin (512 × 64).
- **Esineet:** `content/core/art/objects/<name>.png`, muunnelmat vierekkäin samassa koossa. Koot ja keskipisteet kuvaa `content/core/art/objects/objects.json`:
  - `spruce` 96 px, 3 kpl
  - `birch` 80 px, 3 kpl
  - `rock` 32 px, 4 kpl
  - `bush` 40 px, 4 kpl
  - `shadow` 64 px, 1 kpl (pehmeä musta ellipsi, alfagradientti)
  - `blood` 64 px, 2 kpl (veritahra kaatuneen alle)
- **Maasto:** `content/core/art/terrain/<terrain>.png`, 128 × 128 px, saumaton.

## 6. Näyttö ja käyttöliittymä

**Skaalaus**
- **Venytystila:** Godotissa `canvas_items` / `expand`, peruskoko 1280 × 800.
- **Ikkuna:** avautuu 90 %:iin näytön käytettävästä alasta, keskitetään ja on koon muutettavissa.
- **Kokoruutu:** F11 vaihtaa kokoruututilaan ja takaisin.
- **Retina:** koska koko kuva skaalataan ikkunan mukaan, sekä maailma että tekstit ovat Retinalla noin kaksi kertaa aiempaa suurempia.

**Teema:** perusfontti 18 px, tummat oliivinsävyiset paneelit, joissa vaalea reunus.

**Paneelit**
- **Yläpalkki:** aika, nopeus, tauko, havaintojen yhteenveto ja vihje "F1 help".
- **Alapalkki:** muotokuvakortti jokaisesta omasta sotilaasta, jossa on muotokuva, nimi (esim. "Alik. Korpela") ja tila (asento ja liike). Kortin klikkaus valitsee sotilaan, Shift lisää valintaan ja tuplaklikkaus keskittää kameran.
- **Ohje:** näppäinohje avautuu omaan paneeliin F1:llä.

**Merkinnät kartalla**
- **Valinta:** keltainen ellipsi jalkojen alla ja katkoviivainen reitti.
- **Kuultu:** oranssi katkoviivaympyrä (5 m) ja iso "?".
- **Viimeksi nähty:** vihollisen haamuhahmo 40 %:n peittävyydellä ja "?".

## 7. Testaus

- **Python** (`python3 -m unittest discover -s tools/art/tests`):
  - kvantisointi käyttää vain paletin värejä
  - tekstuurit ovat saumattomia
  - arkkien mitat vastaavat JSON-tiedostoa
  - jokaisessa ruudussa on piirrettyä sisältöä
  - alfa on kaksitasoinen
  - generointi on deterministinen
- **C#** (`Nmf.Client.Tests`):
  - arkin metatietojen lataus ja ruutujen laskenta
  - suunnan seuranta
  - animaation valinta ja askeltahti
  - puiden ja esineiden deterministinen sijoittelu
  - nimet
  - maaston paikat shaderissa
- **Godot:** käännös, savutesti ilman ikkunaa ja ikkunallinen kuvakaappaus, joka tarkistetaan silmämääräisesti.

## 8. Rajattu pois

- räjähdykset, tuliefektit ja veri (vaihe 3)
- minikartta ja rakennukset
- asennon vaihdon välianimaatio (asento vaihtuu suoraan)
- käsin piirretyt kuvat (formaatti mahdollistaa ne myöhemmin)
