# Äänet (eepos 1, vaiheet 5a–5f)

Peliin tulevat äänitehosteet, puhe, musiikki ja ympäristöäänet sekä asetusikkuna, vaikeustaso ja näppäinten
uudelleenmääritys. Käyttäjä hyväksyi rakenteen 9.10.2026. Tämä spec kuvaa koko eepoksen rungon ja vaiheen 5a
tarkasti. Vaiheet 5b–5f saavat omat tarkemmat specinsä, kun niiden vuoro tulee.

Pelissä ei ole nyt yhtään ääntä. Simulaatio tuottaa jo tapahtumat, joihin äänet sidotaan (`ShotFired`,
`GrenadeThrown`, `GrenadeExploded`, `UnitWounded`, `MeleeStarted`, `UnitLooted`, `CounterattackStarted`), ja
`CombatEffects.AddSignals` päättää jo nyt, mitä pelaaja huomaa. Äänet rakennetaan samalle periaatteelle.

## Käyttäjän linjaukset

- Äänet ja musiikki tuotetaan tekoälyllä (esimerkiksi ElevenLabsin äänitehosteet, Suno tai Udio musiikkiin). Käyttäjä
  tuottaa tiedostot ohjeen perusteella samaan tapaan kuin sotilasspritet. Avointa jakelua varten käytetään maksullista
  tasoa tai tarkistetaan palvelun ehdot ennen kuin tiedostot menevät repoon.
- Musiikki: valikossa ja briefingissä teema, taistelussa hiljainen tunnelmamatto ilman tilannemuutoksia, tehtävän
  lopussa voitto- tai tappiotunnus. Mukautuva taistelumusiikki on myöhempi lisä.
- Puhe: taisteluhuudot suomeksi ja venäjäksi sekä käskyjen kuittaukset.
- Asetusikkunaan tulevat voimakkuudet, kieli, näyttö, peliasetukset ja näppäinten uudelleenmääritys. Vaikeustaso
  säätää omien ja vihollisen etukertoimia, ja se valitaan myös tehtävävalikossa.

## Arkkitehtuuri

Äänilogiikka on kokonaan clientissä. Simulaatio ei tiedä äänistä mitään, joten se pysyy deterministisenä ja uusinnat
toimivat kuten ennen. Vaiheen 5a ainoa simulaatioon tuleva muutos on uusi tiedotustapahtuma `ReloadStarted`. Tapahtumat ovat
pelkkää tulostetta, joten ne eivät vaikuta simulaation tilaan.

```
SimEvent[] + käskyt + kamera ──► AudioDirector (Nmf.Client) ──► SoundCue[] ──► AudioView (Nmf.Game, Godot)
                                       ▲
                    AudioCatalog (content/core/audio/sounds.yaml, Nmf.Content)
```

`AudioDirector` saa joka ruudunpäivityksellä ruudun simulaatiotapahtumat, pelaajan antamat käskyt, kameran
keskipisteen ja näkymän koon. Se palauttaa listan soitettavia vihjeitä (`SoundCue`: tunnus, tiedosto, paikka,
voimakkuus dB, väylä, tärkeys, viive). Kaikki päätökset siitä, mitä kuuluu, kuinka kovaa ja milloin,
tehdään tässä luokassa. Siksi ne voidaan testata xUnitilla ilman Godotia.

`AudioView` on ohut Godot-kerros. Siinä on pooli `AudioStreamPlayer`-soittimia ja väylät Master, Sfx, Voice,
Ambience, Music ja Ui. Tiedostot ladataan ajon aikana `AudioStreamOggVorbis.LoadFromFile`-kutsulla content-kansiosta,
kuten kartat ja aseet nyt. Godotin import-vaihetta ei tarvita, ja julkaisupaketti saa äänet mukaan, koska release
kopioi content-kansion kokonaan. Voimakkuuden laskee `AudioDirector`, ja panoroinnin hoitaa Godot: soitin on
`AudioStreamPlayer2D`, joka asetetaan vihjeen paikkaan maailmassa. Sen oma vaimennus on pois päältä (`Attenuation` 0,
`MaxDistance` suuri), joten Godot vain panoroi äänen sen mukaan, missä kohtaa ruutua paikka on. Panorointi tarkistetaan
korvalla testikentällä, ei yksikkötesteillä.

## Kuultavuus: äänet eivät paljasta vihollista

Pelaaja kuulee vain sen, minkä hänen oma puolensa voisi kuulla. Säännöt ovat samat kuin simulaation kuulomallissa
(`VisionSystem.UpdateHearing`).

| Lähde | Kuuluu, kun |
|---|---|
| Oma mies (laukaus, lipas, askeleet, kranaatti, ryöstö) | aina |
| Vihollisen laukaus | joku oma elossa oleva mies on aseen `noise_m`-säteen sisällä (kivääri 300 m, pikakivääri 400 m) |
| Räjähdys | joku oma mies on 800 metrin sisällä |
| Luodin sivallus (`bullet.crack`) | laukaus päätyy 5 metrin päähän omasta miehestä |
| Osuma maahan, puuhun tai veteen | kohta on 40 metrin sisällä omasta miehestä tai ampuja näkyy pelaajalle |
| Vihollisen hiljaiset äänet (askeleet, lipas, ryöstö) | vihollinen näkyy pelaajalle (`IsShownToPlayer`) |
| Lähitaistelu | joku oma mies on mukana tai taistelu näkyy |

Vihollisen laukaus kuuluu siis kauempaa kuin vihollinen näkyy, kuten oikeastikin. Pelaaja saa äänestä saman tiedon kuin
miehet: jossain ammutaan. Laukaus ei kuitenkaan anna tarkkaa paikkaa. Panorointi ja voimakkuus lasketaan karkeasta
paikasta, eli 10 metrin ruudun keskeltä, kuten epäillyn kontaktin merkki (`RoughPosition`). Näkyvän ampujan laukaus
kuuluu tarkasta paikasta.

## Miksauksen säännöt

Kuuntelija on kameran keskellä. Etäisyys mitataan näkymän koossa eikä metreinä, joten zoomaus toimii luontevasti:
ruudulla oleva ääni kuuluu täysillä, ja ruudun ulkopuolinen hiljenee.

- `r` = etäisyys kameran keskeltä jaettuna näkymän puolikkaalla lävistäjällä (maailman metreissä).
- Kun r ≤ 1, voimakkuus on vihjeen oma `volume_db`. Kun r kasvaa arvosta 1 arvoon 3, voimakkuus laskee lineaarisesti
  desibeleinä lattiaan asti.
- Lattia on -18 dB vihjeille, joilla on `loud: true` (laukaukset, räjähdykset, huudot). Ruudun ulkopuolinen taistelu
  kuuluu siis aina hiljaa. Muut vihjeet hiljenevät r = 2:ssa kokonaan.
- Kun zoom on alle 0,4 (kaukonäkymä, jossa nyt näkyvät punaiset pallot), askeleet, lippaat ja ryöstöt jäävät pois.

Lähi- ja kaukoääni valitaan sen mukaan, kuinka kaukana lähin oma mies on: yli 150 metrin päästä soi `shot_far.<luokka>`.
Ääni kuuluu siis sellaisena kuin miehet sen kuulevat. Myös äänen viive lasketaan lähimmästä omasta miehestä
(etäisyys / 343 m/s pelin aikaa). 300 metrin päästä laukaus kuuluu siis 0,9 sekuntia välähdyksen jälkeen. Viive kulkee
pelin ajassa, joten tauon aikana viivästetyt äänet odottavat ja soivat, kun peli jatkuu.

Laukausvihje haetaan aseen tunnuksella (`shot.mosin_m39`). Jos sitä ei ole, haetaan aseluokalla (`shot.rifle`).
Modaaja voi siis lisätä aseen ilman omaa ääntä, ja se kuulostaa luokkansa yleiseltä aseelta.

## Rajoitukset, nopeus ja tauko

- Samanaikaisia soittimia on 32. Kullakin vihjeellä on `max_voices` (oletus 6). Kun raja tulee vastaan, uusi ääni
  korvaa hiljaisimman saman tai matalamman tärkeyden äänen. Jos sellaista ei ole, uusi ääni jää soittamatta.
- Saman ampujan laukausten väli on vähintään 50 ms oikeaa aikaa. Nopeudella x8 sarjat harvenevat, mutta laukaukset
  eivät kasaannu yhdeksi pauhuksi.
- Nopeudella x4 ja x8 askeleet jäävät pois.
- Tauko pysäyttää Sfx- ja Voice-väylien soittimet ja jatkaa niitä, kun peli jatkuu. Ambience ja Music jatkavat 6 dB
  hiljempaa (vaihe 5c).
- Versio valitaan satunnaisesti clientin omalla generaattorilla. Simulaation `Rng` ei saa tulla käyttöön, koska se
  rikkoisi determinismin. Sama versio ei toistu kahdesti peräkkäin, ja sävelkorkeus vaihtelee ±4 %.
- Automaattiajoissa (`--screenshot-at`, `--demo`) äänet ovat pois. Lisäksi tulee `--mute`.

## Äänikirjasto: `content/core/audio/sounds.yaml`

```yaml
# Vihjeet ja niiden tiedostot. Tyhjä files-lista = vihje on hiljaa, kunnes tiedosto tuodaan.
cues:
  shot.mosin_m39:  { bus: sfx, files: [sfx/shot.mosin_m39.1.ogg, sfx/shot.mosin_m39.2.ogg], volume_db: 0, priority: 70, loud: true }
  shot.rifle:      { bus: sfx, files: [], priority: 70, loud: true }
  impact.wood:     { bus: sfx, files: [], volume_db: -6, priority: 30, max_voices: 4 }
  footstep.forest: { bus: sfx, files: [], volume_db: -14, priority: 10, max_voices: 4 }

# Maastotyyppi (kartan TerrainNames) -> askelpinta
surfaces: { forest: forest, grass: grass, road: road, swamp: swamp, water: water }
```

`AudioCatalogLoader` (Nmf.Content) lukee tiedoston. Jos väylä on tuntematon tai `volume_db` on yli +6, lataus antaa
virheen samaan tapaan kuin aseiden lataus. Puuttuva tiedosto ei kaada peliä: vihje on hiljaa, ja loki varoittaa kerran.
Ydinsisällön testi tarkistaa, että jokainen listattu tiedosto löytyy.

## Vaiheen 5a vihjeet

| Vihje | Tapahtuma | Versiot |
|---|---|---|
| `shot.<ase>` × 6 (Suomi, M/39, Pikakivääri M/26, PPŠ-41, Mosin 91/30, DP-27) | `ShotFired` | 3 |
| `shot_far.rifle`, `shot_far.smg`, `shot_far.lmg` | `ShotFired` yli 150 m | 3 |
| `bullet.crack` | `ShotFired`, osuma 5 m:n päähän omasta | 4 |
| `impact.ground`, `impact.wood`, `impact.water`, `impact.body` | `ShotFired`: `Hit` tai solun maasto ja este kohdassa `To` | 3 |
| `reload.rifle`, `reload.smg`, `reload.lmg` | `ReloadStarted` (uusi) | 2 |
| `grenade.throw` | `GrenadeThrown` | 2 |
| `grenade.explode`, `grenade.explode_far` | `GrenadeExploded` | 3 |
| `body.fall` | `UnitWounded`, taso vähintään `Incapacitated` | 3 |
| `melee.scuffle` | `MeleeStarted` | 2 |
| `loot.rustle` | `UnitLooted` | 2 |
| `footstep.<pinta>` × 5, `crawl.rustle` | clientin oma: yksikkö liikkui tällä ruudulla | 4 |
| `ui.click`, `ui.paper` | käsky annettu; käskypaperi B tai kartta M auki tai kiinni | 2 |

Askeleet lasketaan clientissä. Jokaisella liikkuvalla miehellä on askelajastin pelin ajassa: käynti 0,55 s, juoksu
0,35 s, hiipiminen 0,8 s, ryömiminen 1,0 s (`crawl.rustle`). Pinta tulee solun maastotyypistä `surfaces`-taulun kautta.
Askeleita soitetaan vain neljältä kameran keskustaa lähimmältä mieheltä, jotta seitsemän miehen ryhmä ei rätise.

Simulaatioon tulee yksi uusi tapahtuma: `ReloadStarted(Tick, Unit)`, joka lähetetään `Firing.StartReloadOrStop`-kohdasta.
Muutaman testin (esimerkiksi `SimulationTests`, `MovementTests`) vertailut koskevat tarkkoja tapahtumalistoja. Ne
tarkistetaan, mutta ne eivät ammu, joten uusi tapahtuma ei näy niissä.

## Ääniohje ja tuontityökalu

`docs/audio/sound-brief.md` on ohje, jonka perusteella käyttäjä tuottaa äänet. Malli on sama kuin
`docs/art/character-sprite-brief.md`. Ohjeessa on

- yleinen tyyli: 1940-luku, ulkona kesäisessä havumetsässä, kuiva ääni ilman pitkää kaikua, mono, ei musiikkia eikä
  puhetta tehosteissa
- jokaiselle vihjeelle englanninkielinen kehote, pituus, versioiden määrä ja huomiot (esimerkiksi "Suomi KP/-31:
  71 patruunan rumpulipas, korkea tulinopeus noin 900 laukausta minuutissa, yksi laukaus, ei sarjaa")
- tiedostonimet muodossa `<vihje>.<n>.<pääte>`, esimerkiksi `shot.mosin_m39.1.mp3`
- lisenssiohje: kirjaa palvelu, taso ja päivämäärä
- kaksi kierrosta. Ensimmäinen kierros on noin 20 tiedostoa, joilla peli kuulostaa jo elävältä: yksi laukaus kustakin
  kuudesta aseesta, kolme kaukolaukausta, räjähdys lähellä ja kaukana, luodin sivallus, osuma puuhun ja maahan, kaatuminen
  ja kranaatin heitto. Toinen kierros on loput versiot, lippaat, askeleet ja käyttöliittymä. Kaikkiaan 5a:n vihjeissä on
  noin 90 tiedostoa.

Ohje kirjoitetaan 5a-suunnitelman ensimmäisenä tehtävänä, jotta käyttäjä voi tuottaa ääniä sillä aikaa, kun järjestelmää
rakennetaan.

`python3 -m tools.audio.import <raakakansio>` tuo tiedostot peliin. Työkalu

1. lukee mp3- tai wav-tiedostot ja tunnistaa vihjeen nimestä
2. leikkaa hiljaisuuden alusta ja lopusta (raja -50 dBFS)
3. muuntaa tehosteet ja puheen monoksi (musiikki ja ympäristö pysyvät stereona)
4. normalisoi huipun -1 dBFS:ään
5. kirjoittaa Ogg Vorbis -tiedoston (laatu 5) kansioon `content/core/audio/<väylä>/`
6. lisää tiedoston `sounds.yaml`-listaan
7. kirjaa rivin `content/core/audio/SOURCES.md`-tiedostoon (tiedosto, palvelu, päivämäärä, kehote, lisenssi).

Työkalu tarvitsee `pip install soundfile`. Sen libsndfile lukee mp3:n ja kirjoittaa Ogg Vorbisia, eikä ffmpegiä
tarvita. Testit ajetaan synteettisellä siniäänellä (`tools/audio/tests`).

Vaiheessa 5a peliin ei tule valmiita ääniä. Järjestelmä on valmis ja testattu, ja peli on hiljaa, kunnes käyttäjä
tuottaa tiedostot ohjeen perusteella. Toiston toimivuus tarkistetaan yhdellä testin luomalla siniäänellä
testikentällä.

## Testaus (5a)

- `AudioDirectorTests` (Nmf.Client.Tests):
  - piilossa olevan vihollisen laukaus, kun oma mies on `noise_m`-säteen ulkopuolella: ei vihjettä
  - säteen sisällä: vihje karkeasta paikasta
  - näkyvä ampuja: tarkka paikka
  - lähi- ja kaukoääni 150 metrin rajalla, viive 343 m/s, viivästetty ääni odottaa tauon yli
  - voimakkuus r = 1, 2 ja 3, `loud`-lattia
  - äänirajat ja korvaus
  - saman ampujan 50 ms:n väli
  - askeleet nopeudella x4 ja zoomilla alle 0,4 pois
  - aseen tunnuksesta luokkaan putoava haku
  - sama versio ei toistu.
- `AudioCatalogLoaderTests` (Nmf.Content.Tests): kelvollinen tiedosto, tuntematon väylä, liian kova `volume_db`,
  puuttuva tiedosto antaa varoituksen.
- `CoreContentTests`: `sounds.yaml` latautuu ja kaikki listatut tiedostot löytyvät.
- Simulaatio: `ReloadStarted` lähetetään, kun lipas vaihdetaan, eikä kun ammukset loppuvat.
- `tools/audio/tests`: tuonti leikkaa, normalisoi, muuntaa monoksi ja kirjaa lähteen.

## Myöhemmät vaiheet (runko)

**5b Puhe.** Taisteluhuudot suomeksi ja venäjäksi, esimerkiksi "Käsikranaatti!", "Lipas!", "Mies kaatui!",
"Lääkintämies!", "Урааа!", "Граната!" ja tuskanhuudot. Lisäksi käskyjen kuittaukset, kuten "Selvä!", "Mennään!" ja
"Tulta!". Kuittaukset jaetaan liikkeeseen, hyökkäykseen, tuleen, pysähtymiseen ja asentoon. Jokaiselle miehelle
valitaan äänityyppi (2–3 ääntä per puoli). Valinta perustuu nimeen eikä satunnaisuuteen, jotta mies kuulostaa aina
samalta. Vihollisen huudot noudattavat yllä olevaa kuultavuussääntöä. Ratkaistavaksi jäävät toiston esto, viive ja se,
vastaako kuittaukseen yksi mies vai ryhmänjohtaja. Lisäksi pitää ratkaista, kuuluvatko kuittaukset tauon aikana.
Pelaaja antaa usein käskyt tauolla, ja käskypaperi B avautuu tauolle, mutta 5a pysäyttää Voice-väylän tauon ajaksi.

**5c Musiikki ja ympäristö.** Valikon ja briefingin teema, taistelun tunnelmamatto, ympäristöäänet (tuuli, metsä,
linnut, kaukainen tykistö), voitto- ja tappiotunnus sekä liukuvat siirtymät. Ympäristö voi myöhemmin riippua
tehtävän vuodenajasta ja kellonajasta (`start`), kun talvitehtävät tulevat.

**5d Asetusikkuna.** Voimakkuudet väylittäin, kieli (nykyinen `--lang` toimii edelleen ja ohittaa asetuksen), näyttö
(ikkuna tai koko näyttö, UI-skaalaus) ja peliasetukset (oletusnopeus, korttien koko, seuranta oletuksena sekä uutena
toimintona automaattinen tauko kontaktissa, jota pelissä ei vielä ole).
Tallennus `user://settings.json`-tiedostoon samaan tapaan kuin `progress.json`. Ikkuna aukeaa
päävalikosta ja Esc-valikosta.

**5e Vaikeustaso.** Esimerkiksi tasot Helppo, Normaali ja Vaikea, jotka säätävät omien ja vihollisen kertoimia.
Kertoimet (osumatarkkuus, moraali, havaitseminen tai muut) valitaan 5e:n omassa specissä mittarin avulla. Taso
valitaan tehtävävalikossa, oletus tulee asetuksista, ja edistyminen tallentaa, millä tasolla tehtävä on läpäisty.
Taso on simulaation syöte, joten se kirjataan uusintaan.

**5f Näppäinten uudelleenmääritys.** Käskynäppäinten vaihto asetuksissa. Samalla korjataan aiemmin auki jäänyt asia:
näppäinkoodit riippuvat nyt näppäimistöasettelusta.
