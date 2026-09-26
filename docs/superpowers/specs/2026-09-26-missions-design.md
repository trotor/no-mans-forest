# No Man's Forest – tehtävät, tehtäväkäsky, kartta ja sotilaiden ominaisuudet (vaihe 4c)

Päivämäärä: 2026-09-26
Tila: käyttäjän pyynnöt ("peliin tarvitaan avattava kartta, josta voi katsoa tehtävän sisältöä, ja tehtäväpaperi, jossa on aina tehtävän kuvaus"; "lisää omien miesten rohkeutta, ne ovat sankaripoikia tässä ekassa tehtävässä – monipuolista hahmojen ominaisuuksia tarvittaessa"), toteutetaan suoraan
Liittyy: pääspeksi §10.3 (mission.yaml) ja §11 (Iskuosasto)

## 1. Tavoite

- **Tehtävä data-kansiona:** `content/core/missions/<id>/` sisältää `mission.yaml` ja käskyt `briefing.en.md` / `briefing.fi.md`.
- **Tehtäväkäsky:** paperi aukeaa tehtävän alussa (peli tauolla) ja aina näppäimellä B tai yläpalkin painikkeesta. Siinä ovat tilanne, tehtävä, toteutus, kokoonpano ominaisuuksineen ja tavoitteet valintaruutuineen.
- **Avattava kartta:** näppäin M tai yläpalkin painike avaa koko alueen paperikarttana merkintöineen. Klikkaus karttaan siirtää kameran siihen kohtaan.
- **Tavoitteet ja tulos:** tavoitteita seurataan, ja tehtävä päättyy onnistumiseen tai epäonnistumiseen.
- **Sotilaiden ominaisuudet:** määritellään tehtävässä miehittäin. Iskuosaston suomalaiset ovat sankaripoikia.

**Rajattu pois:**
- tapahtumat ("kun … niin …") ja Lua
- vuoropohjainen kontaktitila
- kranaatinheitintuki
- tehtävävalikko: tehtävä valitaan komentoriviltä `--mission=` (oletus `iskuosasto`)

## 2. mission.yaml (osajoukko pääspeksin §10.3:sta)

```yaml
id: iskuosasto
title: { en: "Strike Detachment", fi: "Iskuosasto" }
date: { en: "Onega shore south of Karhumäki, July 1942, dawn", fi: "..." }
map: karhumaki                         # content/core/maps/<map>.tmx
briefing: { en: briefing.en.md, fi: briefing.fi.md }
forces:
  player:                              # map points of type "blue" in order
    - { name: "Alik. Korpela", weapon: suomi_kp31, grenade: m32, leader: true,
        nerve: 95, morale: 950, marksmanship: 70, leadership: 90 }
  enemy:                               # points of type "red"; the first "patrol" path goes to the nearest one
    - { name: "Serzhant Belov", weapon: ppsh41, grenade: rgd33, leader: true, items: [soviet_orders] }
objectives:
  - { id: grab_orders, type: pick_up, item: soviet_orders, text: { en: "...", fi: "..." } }
  - { id: bring_back, type: reach_zone, zone: start_zone, carrying: soviet_orders, requires: [grab_orders], text: { ... } }
items:
  soviet_orders: { en: "Soviet orders", fi: "Neuvostopartion käskyt" }
```

**Tarkistukset** (virheilmoitus tiedostonimen kanssa):
- tuntematon ase tai kranaatti
- enemmän miehiä kuin lähtöpisteitä kartalla
- tuntematon tavoitetyyppi tai alue, jota kartalla ei ole
- `requires` viittaa tuntemattomaan tavoitteeseen tai muodostaa silmukan
- ominaisuus alueen ulkopuolella
- puuttuva englanninkielinen teksti

## 3. Sotilaiden ominaisuudet

| Ominaisuus | Arvot | Oletus | Vaikutus |
|---|---|---|---|
| `nerve` (sisu) | 0–100 | 50 | ≥ 75: pysyy paikallaan tulen alla (vaihe 3d). Lisäksi lamautumisen, toipumisen ja maahan menemisen rajat kerrotaan (100 + sisu − 50) %:lla (rajattu 60–150 %, esim. sisu 95 → lamautuu 580:sta 400:n sijaan), ja moraalitestiin lisätään (sisu − 50) × 4 (sisu 95 → +180). |
| `morale` (moraali) | 0–1000 | 700, johtajalla 800 | lähtömoraali ja taso, jolle moraali palautuu |
| `marksmanship` (ampumataito) | 0–100 | 50 | hajonta × (150 − taito) / 100: taito 50 → 100 %, 90 → 60 %, 20 → 130 % |
| `leadership` (johtamiskyky, vain johtajalla) | 0–100 | 100 | johtajan laatu (kokoaminen ja moraalibonus, vaihe 3) |
| `experience` (kokemus) | 0–100 | 50 | liikkeessä vaikeampi maali, nopeammat asennonvaihdot, tehokkaampi suojatuli, maaten ampuminen (vaihe 4g, `2026-09-26-experience-prone-design.md`) |

**Iskuosaston kokoonpano:**
- **Suomalaiset:**

  | Mies | Ase | Sisu | Moraali | Ampumataito | Johtamiskyky |
  |---|---|---|---|---|---|
  | Alik. Korpela | KP/31 | 95 | 950 | 70 | 90 |
  | Kpl. Virtanen | Lahti-Saloranta | 85 | 900 | 75 | – |
  | Sotm. Mäkinen, tarkka-ampuja | M/39 | 80 | 880 | 90 | – |
  | Sotm. Laine | M/39 | 90 | 900 | 60 | – |

- **Neuvostopartio:** tavalliset miehet, joiden sisu on 40–75 ja moraali 650–800. Johtajalla on käskyt.

## 4. Tavoitteet ja tulos

**`pick_up`:** valmis, kun joku oma toimintakykyinen mies kantaa esinettä. Jos kantaja kaatuu ennen kuin seuraava tavoite on valmis, tavoite palautuu keskeneräiseksi, koska paperit ovat nyt hänen ruumiillaan.

**`reach_zone`:** valmis, kun oma toimintakykyinen mies on alueella ja kaikki `requires`-tavoitteet ovat valmiina. Jos alueella on `carrying`, miehen täytyy kantaa kyseistä esinettä.

**Tulos:**
- **Onnistui:** kaikki tavoitteet ovat valmiina.
- **Epäonnistui:** kaikki omat miehet ovat toimintakyvyttömiä.

Tulos on lopullinen, eli tehtävän päätyttyä tilaa ei enää muuteta. Peli jatkuu kuitenkin, jos pelaaja haluaa katsella.

**Tapahtumat:** `ObjectiveChanged(tick, id, done)` ja `MissionEnded(tick, success)`. Tarkistus on deterministinen ja tehdään joka askeleen jälkeen.

## 5. Kartta

- **Alueet:** generaattori lisää kartalle `start_zone`-alueen (lähtöalue, 40 × 30 m suomalaisten ympärillä) ja `outpost`-alueen (ilmoitettu vihollisasema, 70 × 50 m, keskitetty 10 m sivuun todellisesta, koska tiedustelu on epätarkka).
- **Paperikartta:** piirretään kartasta kerran 2 m/pikseli:
  - maastovärit: metsä vaaleanvihreä, avoin kellertävä, suo sinisin raidoin, vesi sininen
  - ruskeat korkeuskäyrät 5 m välein, joka viides paksumpana
  - tiet ruskeina
  - kivet mustina pisteinä
- **Merkinnät kartan päällä:**
  - omat miehet sinisinä pisteinä nimikirjaimin
  - nähdyt viholliset punaisina, viimeksi nähdyt "?"-merkillä
  - alueet katkoviivoin nimineen
  - tavoitteen kohde merkitty
  - pohjoisnuoli ja 100 m mittakaava
  - näkyvä ruudun osa kehyksenä

## 6. Näkymä

**Yläpalkki:** "Objectives 1/2" sekä painikkeet "Orders (B)" ja "Map (M)".

**Tehtäväkäsky:** paperinvärinen paneeli, jossa on otsikko, aika ja paikka, käskyn teksti (markdownin otsikot ja kappaleet), kokoonpano ominaisuuksineen ja tavoitteet (☐/☑).

**Ilmoitukset:**
- tavoitteen valmistuessa lyhyt teksti keskellä ruutua
- tehtävän päättyessä paneeli: "Mission accomplished" / "Mission failed" ja tappiot

**Kieli:** `--lang=fi|en` (oletus en) valitsee tehtävän tekstien kielen. Muu käyttöliittymä on englanniksi.

## 7. Testaus

- **Nmf.Sim:**
  - ominaisuudet: hajonta ampumataidon mukaan ja moraalin palautuminen omaan perustasoon
  - kokoonpano ja esineet
  - tavoitteet: `pick_up`, kantajan kaatuminen, `reach_zone` + `carrying` + `requires`
  - tulos ja lopullisuus
  - deterministisyys
- **Nmf.Content:**
  - mission.yaml: kaikki kentät ja virheet
  - Iskuosasto: latautuu ja on yhteensopiva kartan kanssa
- **Python:** alueet kartalla.
- **Nmf.Client:**
  - paperikartan värit ja korkeuskäyrät
  - markdownin muunnos
  - tehtäväpaperin teksti ja tavoitteiden tila
  - miehen nimi kokoonpanosta
- **Godot:** käännös, savutesti ja kuvakaappaukset käskystä ja kartasta.
