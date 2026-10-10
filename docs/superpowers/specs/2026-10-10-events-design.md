# Tapahtumat, laukaisimet ja uudet tavoitteet (vaihe 8a, skenaarioiden eepos)

Skenaarioiden eepos on kuvattu specissä 2026-10-09-roadmap-epics (eepos 3). Tämä on sen ensimmäinen vaihe, ja kaikki
myöhemmät nojaavat siihen. Pääspecin §10.3 esitteli "kun … niin …" -tapahtumat. Tässä ne toteutetaan, ja mukaan
tulevat tavoitteet "löydä kohde" ja "pelasta mies ja tuo takaisin". Käyttäjä 10.10.2026: "tee päätöksiä ja ala
toteuttaa".

## 1. Determinismi

- **Kaikki tapahtuu käskyjen kautta.** Tapahtumaskripti (`MissionEvents`) ajetaan ennen jokaista askelta samaan tapaan
  kuin partiot ja vihollisen komentaja. Uusinta toistaa käskyt.
- **Vahvistukset eivät synny tyhjästä.** Ne ovat tehtävän joukoissa alusta asti omana ryhmänään (esimerkiksi
  `reserve`) taempana kartalla, ja tapahtuma lähettää ne liikkeelle. Vihollisen komentaja ei käytä niitä, ennen kuin
  ne on päästetty (`release`).
- **Löydetty mies** (`stranded`) on simulaation tilaa. `MissionEvents` vapauttaa hänet, kun joku pelaajan miehistä
  on enintään 5 metrin päässä. Sen jälkeen hän on pelaajan komennettavissa.

## 2. `mission.yaml`

```yaml
places:                      # metreinä kartan vasemmasta yläkulmasta
  wreck: [612, 340]
zones:                       # suorakulmiot metreinä; lisäävät kartan alueita
  home: { min: [400, 560], max: [520, 640] }
  search: { min: [560, 280], max: [680, 400] }

forces:
  player:
    - { name: "Luutn. Saarinen", weapon: pistol, stranded: true, state: wounded, at: [612, 344], squad: pilot }
  enemy:
    - { name: "Kers. Petrov", squad: reserve, leader: true, at: [700, 120] }

objectives:
  - { id: find_wreck, type: find, place: wreck, area: search, text: { … } }
  - { id: bring_back, type: rescue, unit: "Luutn. Saarinen", zone: home, requires: [find_wreck], text: { … } }
  - { id: papers, type: pick_up, item: maps, hidden: true, text: { … } }    # näkyy vasta, kun tapahtuma paljastaa sen

events:
  - id: wreck_found
    when: { objective: find_wreck }
    do:
      - { message: { en: "The wreck! The pilot is alive.", fi: "Hylky! Lentäjä on elossa." } }
      - { wait_s: 120 }
      - { send: { squad: reserve, to: wreck, mode: run } }
      - { release: reserve }
  - id: late
    when: { after_s: 900 }
    do: [ { message: { en: "…", fi: "…" } }, { reveal: papers } ]
```

## 3. Laukaisimet (`when`)

Kaikkien annettujen ehtojen täytyy täyttyä. Tapahtuma laukeaa kerran.

| Ehto | Täyttyy kun |
|---|---|
| `after_s: n` | pelin alusta on kulunut n sekuntia |
| `objective: id` | tavoite on täytetty |
| `event: id` | toinen tapahtuma on lauennut |
| `contact: enemy` / `contact: player` | se puoli on nähnyt toisen puolen miehen |
| `zone: { name, side }` | joku puolen taistelukykyisistä on alueella |
| `losses: { side, at_least: n }` | puolelta on poissa taistelusta vähintään n miestä |

## 4. Toimet (`do`)

Toimet tehdään järjestyksessä. `wait_s` viivästää sitä seuraavia.

| Toimi | Vaikutus |
|---|---|
| `wait_s: n` | seuraavat odottavat n sekuntia |
| `message: { en, fi }` | `MissionMessage`-tapahtuma: pelaajalle ilmoitus ja merkintä lokiin |
| `send: { squad, to, mode }` | ryhmän taistelukykyisille `MoveOrder` paikkaan tai alueen keskelle (`walk`, `run`, `sneak`) |
| `release: squad` | vihollisen komentaja ottaa ryhmän omakseen, ja sen koti on paikka, jossa se on silloin |
| `reveal: objective` | piilotettu tavoite tulee näkyviin ja alkaa vaikuttaa tehtävän tulokseen |
| `end: success` / `end: failure` | tehtävä päättyy |

## 5. Uudet tavoitteet

- **`find`:** täyttyy, kun joku pelaajan taistelukykyisistä on `radius_m` (oletus 15 m) päässä paikasta. Löydetty
  pysyy löydettynä.
  - Opasnuoli ei näytä paikkaa ennen löytämistä (eepoksen reunaehto). Jos `area` on annettu, nuoli näyttää etsittävän
    alueen. Muuten se ei näytä mitään.
- **`rescue`:** täyttyy, kun nimetty pelaajan mies on elossa (haavoittunut käy) alueella. Jos hän kuolee, tehtävä
  epäonnistuu.
- **`hidden: true`:** tavoite ei näy eikä vaikuta tulokseen, ennen kuin `reveal` paljastaa sen.

## 6. Pelaajalle

- Viesti näkyy ruudulla pidempään kuin tavallinen ilmoitus (8 s), ja tehtäväkäskyn (B) lokiin kirjautuvat aiemmat
  viestit.
- Löytämätön mies ei näy korteissa. Kun hänet löydetään, hänelle tulee kortti, ja ilmoitus kertoo *"Löytyi:
  Luutn. Saarinen"*.
- Tavoitelista näyttää vain näkyvät tavoitteet.

## 7. Testit

- **`MissionLoaderTests`:**
  - places, zones, events (jokainen ehto ja toimi), `find`, `rescue`, `hidden` ja `stranded`
  - virheet: tuntematon paikka, alue, ryhmä, tavoite, tapahtuma ja ehto
- **`MissionEventsTests`:**
  - jokainen ehto laukaisee kerran
  - `wait_s`
  - `send` antaa käskyt
  - `release` siirtää ryhmän komentajalle
  - `reveal`
  - `end`
  - löydetty mies liittyy joukkoon
- **`MissionTrackerTests`:**
  - `find`
  - `rescue`, ja kuolema epäonnistuttaa
  - piilotettu tavoite ei estä voittoa ennen paljastusta
- **Client:**
  - viesti ilmoituksena ja lokissa
  - opasnuoli `find`-tavoitteelle (alue tai ei mitään)
  - löytämätön mies ei näy korteissa eikä ole komennettavissa
- **Sisältö:** koetehtävä `tapahtumakoe` (debug: true) karhumäen kartalla. Siinä etsitään hylky, löydetään lentäjä,
  vihollisen reservi lähetetään liikkeelle ja lentäjä tuodaan kotiin.
