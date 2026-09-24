# No Man's Forest – suunnitteludokumentti

Päivämäärä: 2026-09-24
Tila: luonnos, odottaa hyväksyntää

## 1. Tavoite ja rajaus

### 1.1 Mikä peli on

No Man's Forest on 2D-taktiikkapeli ylhäältä päin kuvattuna. Se sijoittuu jatkosotaan (1941–44). Henki on hybridi kolmesta esikuvasta:

- **Close Combat:** reaaliaikainen, ryhmä- ja sotilastason taistelu, jossa moraali ja lamautus ratkaisevat.
- **Jagged Alliance 2:** nimetyt hahmot ja vuoropohjainen tila kontaktissa, toimintapisteet.
- **Squad Leader:** johtajat kertoimina, portaittainen moraalitila, tuli tuottaa vaikutuksia, vastatuli.

Pelaaja katsoo taistelua ylhäältä ja antaa käskyjä. Sotilaat toteuttavat ne itsenäisesti.

### 1.2 Taustat ja reunaehdot (käyttäjän linjaukset)

- Harrastusprojekti, joka voidaan myöhemmin julkaista ilmaiseksi.
- Koodi julkaistaan avoimena, ja muiden pitää voida tehdä helposti lisätehtäviä.
- Pääalusta on macOS. Windowsin ja Linuxin pitää myös toimia.
- Grafiikka on 2D ylhäältä päin, nätisti animoituna ja matalalla resoluutiolla.
- Mukana ovat myös ajoneuvot, lähitaisteluaseet, panssarit ja epäsuora tuli.
- Näkyvyys on rajattu (fog of war). Tehtävät ovat lyhyitä missioita.

### 1.3 Onnistumisen mittari ensimmäiselle versiolle

Tehtävä "Iskuosasto" on pelattavissa alusta loppuun ja tuntuu jännittävältä. Siinä toimivat ja tuntuvat oikeilta:

- sumu, äänet ja hiiviskely
- reaaliaika + tauko sekä vuoropohjainen tila kontaktissa, ja vaihto niiden välillä
- lamautus, moraali ja johtajat
- lähitaistelu juoksuhaudassa
- kranaatinheitintulen tilaaminen
- yksi vihollisen panssarivaunu ja keinot sitä vastaan

### 1.4 Rajattu ensimmäisen version ulkopuolelle

- Verkkopeli (arkkitehtuuri pitää sen mahdollisena: deterministinen simulaatio)
- WEGO-aikamuoto (lisätään toisessa vaiheessa)
- Strateginen kerros (JA2:n kartta, palkkasoturit, talous, kampanja)
- Talvimaasto, yö ja rakennusten useat kerrokset
- Pelin sisäinen karttaeditori (käytetään Tiledia)
- Lopulliset grafiikat (ensin paikkagrafiikat tai valmiit avoimet paketit)

## 2. Teknologia

| Osa | Valinta | Peruste |
|---|---|---|
| Moottori | Godot 4 (.NET-versio) | Ilmainen ja avoin, vientiin macOS (Apple Silicon), Windows ja Linux |
| Kieli | C# (.NET 8) | Käyttäjälle tuttu, sama kieli simulaatiossa ja pelissä |
| Simulaatio | Puhdas C#-kirjasto ilman Godot-riippuvuutta | Testattavuus, deterministisyys, komentorivikäyttö |
| Testit | xUnit | .NET-standardi |
| Kartat | Tiled (.tmx) | Valmis, ilmainen, kaikilla alustoilla toimiva editori |
| Data | YAML (YamlDotNet) | Luettava ja helppo muokata käsin |
| Skriptit | Lua (MoonSharp) | C#-toteutus, toimii kaikilla alustoilla, hiekkalaatikoitavissa |
| Lisenssit | Koodi MIT, grafiikka ja äänet CC BY-SA 4.0 | Avoin kehitys |

## 3. Projektin rakenne

```
(repon juuri)/
├─ src/
│  ├─ Nmf.Sim/          # simulaatioydin, ei Godot-riippuvuutta
│  │  ├─ World/         # kartta, solut, maasto, korkeus
│  │  ├─ Units/         # sotilaat, ryhmät, ajoneuvot, varusteet
│  │  ├─ Actions/       # toiminnot ja niiden vaiheet
│  │  ├─ Combat/        # tuli, lamautus, moraali, lähitaistelu, panssari, epäsuora tuli
│  │  ├─ Vision/        # näkölinja, havaitseminen, äänet, tieto per osapuoli
│  │  ├─ Orders/        # käskyt ja komentoketju
│  │  ├─ AI/            # sotilaan tekoäly, johtajan suunnittelija, komentaja
│  │  ├─ Time/          # aikamuodot
│  │  └─ Mission/       # tavoitteet, tapahtumat, Lua-rajapinta
│  ├─ Nmf.Content/      # YAML-, Tiled- ja pakettien lataus sekä tarkistin
│  ├─ Nmf.Game/         # Godot-projekti: piirto, animaatio, UI, ääni, syöte
│  ├─ Nmf.Cli/          # komentorivityökalu: validate, headless-ajot, tilastot
│  └─ Nmf.Sim.Tests/    # yksikkö- ja regressiotestit
├─ content/
│  ├─ core/             # perussisältö pakettina (sisältää Iskuosaston)
│  └─ mods/             # käyttäjien paketit
└─ docs/
```

Riippuvuussuunta: `Nmf.Game` → `Nmf.Content` → `Nmf.Sim`. `Nmf.Cli` käyttää samoja kirjastoja kuin peli. `Nmf.Sim` ei riipu mistään muusta osasta.

## 4. Simulaatioydin

### 4.1 Kiinteä askel ja deterministisyys

- Simulaatio etenee **20 askelta sekunnissa**. Grafiikka interpoloi askelten välit, jolloin animaatio on sulava.
- Simulaatiolla on oma satunnaislukugeneraattori. Kaikki satunnaisuus, myös Lua-skripteissä, tulee siitä.
- Samat alkutila, siemenluku ja käskyjono tuottavat aina saman lopputuloksen. Kriittisissä laskuissa (näkölinja, osumat) vältetään alustariippuvaa liukulukukäyttäytymistä, esim. kiintopisteluvuilla tai rajatuilla liukuluvuilla.
- Yksiköiden käsittelyjärjestys on vakio (tunnisteen mukaan).

### 4.2 Syöte ja tuloste

- **Käskyt ovat ainoa syöte.** Pelaaja, tekoäly ja skriptit antavat simulaatiolle vain käskyjä, kuten `Move`, `Fire`, `CallArtillery`, `SetFirePolicy` tai `SquadAssault`. Käyttöliittymä ei muuta tilaa suoraan.
- **Tapahtumat ovat tuloste.** Simulaatio lähettää tapahtumia, kuten `ShotFired`, `UnitPinned`, `ExplosionAt`, `SoundEmitted` tai `MoraleChanged`. Godot-puoli soittaa niiden perusteella äänet, animaatiot ja efektit.
- **Uusinta** tallennetaan alkutilana, siemenlukuna ja askelittain merkittynä käskyjonona.

### 4.3 Tietorakenne

Sotilaat, ryhmät ja ajoneuvot ovat tavallisia C#-olioita, eikä täyttä ECS-rakennetta käytetä. Mittakaava on enintään muutama sata yksikköä.

## 5. Toiminnot ja aikamuodot

### 5.1 Toiminto on kesto askelina

Askel on simulaation aikayksikkö, ei toiminnon hinta. Jokaisella toiminnolla on vaiheet, ja jokainen vaihe kestää tietyn määrän askelia. Esimerkkejä (20 askelta = 1 s):

| Toiminto | Vaiheet | Kesto |
|---|---|---|
| Tähdätty kivääriammunta | tähtäys 30 → laukaus 1 → lukon veto 20 | noin 2,5 s |
| Pikakiväärin sarja | tähtäys 10 → sarja 20 → palautuminen 10 | 2 s |
| Käsikranaatti | valmistelu 20 → heitto 10 → palautuminen 10 | 2 s |
| Makuulle meno | 20 | 1 s |
| Konekiväärin pystytys | 100 | 5 s |
| Liike 1 m | maasto × asento × väsymys | noin 5–15 askelta |
| Tulen tilaus radiolla | yhteys 60 → käsky 40, sitten viive | noin 5 s + viive |

- Kestoja muokkaavat kokemus, lamautus, haavat ja väsymys.
- Toiminto voi keskeytyä, esim. tähtäys lamautuksen vuoksi. Käytetty aika menee silloin hukkaan.
- Kestot määritellään datatiedostoissa (aseet, yksiköt).

### 5.2 Aikamuodot

Sama toimintomalli toimii kaikissa aikamuodoissa. Aikamuoto määrää vain, kuka saa käyttää aikaa ja milloin.

- **Reaaliaika + tauko** (ensimmäinen versio): kaikki yksiköt etenevät joka askel. Tauko pysäyttää askeleet, ja käskyjä voi silti antaa.
- **Vuoropohjainen** (ensimmäinen versio):
  - Jokaisella sotilaalla on vuorossa aikabudjetti, oletuksena 6 s eli 120 askelta. Käyttöliittymä näyttää sen toimintapisteinä: 1 AP = 5 askelta, joten vuorossa on 24 AP:ta.
  - Kun sotilas toimii, simulaatio ajaa hänen toimintonsa askeleet, ja muut pysyvät paikallaan.
  - **Keskeytys/vastatuli:** joka askeleella tarkistetaan, näkeekö joku toimijan. Jos näkevällä on valmiustila ja budjettia, hän voi keskeyttää ja toimia.
  - Käyttämätöntä budjettia voi siirtää osittain seuraavaan vuoroon valmiustilana.
- **WEGO** (toinen vaihe): molemmat antavat käskyt, sitten ajetaan reaaliaikaa esim. 200 askelta.
- **Hybridi:** tehtävä määrittää oletustilan ja kontaktitilan. Kun kontakti syntyy, peli vaihtaa kontaktitilaan niin, että kesken olevat toiminnot jatkuvat. Kun kontaktia ei ole ollut hetkeen, palataan oletustilaan.

## 6. Komentoketju ja itsenäiset sotilaat

### 6.1 Pelaajan rooli

Pelaaja on ylhäältä katsova komentaja. Hän näkee vain omien yksiköidensä havainnot ja antaa käskyjä, mutta miehet toteuttavat ne itse.

### 6.2 Käskyjen tasot

| Taso | Esimerkki | Yksityiskohdista päättää |
|---|---|---|
| Ryhmä (johtajan kautta) | etene, vyörytä juoksuhauta, pidä alue | johtajan suunnittelija: muodostelma, paikat, suojaus ja liike |
| Sotilas | mene tuohon, ammu tuota | sotilas: reitti, asento, ajoitus |
| Pysyvä toimintatapa | tuliperiaate, asento, liikkumistapa | ohjaa itsenäistä käyttäytymistä |

### 6.3 Itsenäinen käyttäytyminen

Sotilas voi käskyn aikana ja sen jälkeen itsekseen:

- hakea suojaa
- ampua takaisin tuliperiaatteen mukaan
- vaihtaa lippaan sopivalla hetkellä
- mennä maahan kovassa tulessa
- auttaa haavoittunutta, jos käsky sallii
- ilmoittaa havainnoista johtajalle

### 6.4 Totteleminen riippuu tilasta

- **Kunnossa:** tottelee.
- **Lamautettu:** ei nouse eikä liiku avoimeen maastoon, mutta voi ampua suojasta.
- **Murtunut:** perääntyy suojaan eikä tottele. Johtaja voi koota hänet.
- **Paniikki / sankaruus:** harvinaiset moraalitapahtumat.

### 6.5 Johtaja

- **Komentoalue:** oletuksena noin 30 m metsässä. Sen ulkopuolella miehet eivät saa uusia ryhmäkäskyjä.
- **Ominaisuudet:** moraalikerroin, kokoamiskyky ja käskyjen nopeus.
- **Kaatuminen:** ryhmä joutuu moraalitestiin, ja vanhin mies ottaa johdon heikommilla arvoilla.
- **Radiomies:** jatkaa komentoaluetta tukeen ja muihin ryhmiin.

### 6.6 Vuoropohjaisessa tilassa

Ryhmäkäsky muuttuu jokaisen sotilaan toiminnoiksi, jotka käyttävät kunkin omaa budjettia. Pelaaja voi hyväksyä johtajan suunnitelman tai muuttaa yksittäisiä toimintoja ennen ajoa.

### 6.7 Toteutus

- **Johtajan suunnittelija** jakaa ryhmätehtävän miehille.
- **Sotilaan tekoäly** perustuu hyötyfunktioon: se valitsee seuraavan toiminnon käskyn, uhkien, suojan ja moraalin perusteella.
- **Molemmat osapuolet** käyttävät samaa järjestelmää.

## 7. Näkyvyys, äänet ja sumu

### 7.1 Kartan solut

Solu on 1 m × 1 m. Tyypillinen kartta on 400 × 400 m. Solun kentät:

- **maan korkeus** (m)
- **esteen korkeus** (m)
- **näkösuoja** (0–1 per metri)
- **suoja luoteilta** (0–1)
- **maastotyyppi:** liikkuminen ja ääni

### 7.2 Näkölinja

- Säde kulkee katsojan silmistä kohteeseen ja kerää näkösuojaa. Kun summa ylittää rajan, kohdetta ei nähdä.
- Kohteen korkeus riippuu asennosta: seisova 1,7 m, polvillaan 1,0 m, makuulla 0,3 m. Maan ja esteiden korkeudet voivat katkaista säteen.

### 7.3 Havaitseminen

Havainto kertyy ajan kuluessa. Siihen vaikuttavat:

- etäisyys, kasvillisuus ja asento
- kohteen liike ja ammunta
- katsojan kokemus, väsymys ja lamautus
- valo (aamuhämärä, valoraketti)
- savu

### 7.4 Tiedon tasot per osapuoli

- **Tuntematon**
- **Epäilty:** ääni tai merkki, karkea alue
- **Havaittu:** kohde näkyy, tyyppi ei välttämättä selvä
- **Tunnistettu:** tyyppi selvä
- **Viimeksi nähty:** haamumerkintä, joka himmenee

### 7.5 Äänet

Toiminnot lähettävät ääniä, joilla on voimakkuus. Ääni vaimenee etäisyyden mukaan. Kuulija saa suunnan ja karkean paikan, ja tarkkuus heikkenee etäisyyden kasvaessa.

### 7.6 Tiedon kulku

- **Sotilas** toimii omien havaintojensa ja ryhmänsä tiedon perusteella.
- **Ryhmän sisällä** tieto leviää heti komentoalueella.
- **Ryhmien välillä** tieto kulkee radion tai lähetin kautta viiveellä.
- **Pelaaja** näkee kaikkien omien yksiköiden tiedon yhdistettynä.

### 7.7 Suorituskyky

Jokaisen sotilaan näkyvyys päivitetään vuorotellen, oletuksena joka 5. askel. Lähellä olevat kohteet haetaan tilakartalla.

## 8. Taistelumalli

### 8.1 Tuli

- **Laukaus on säde, jolla on hajonta.** Hajontaan vaikuttavat ase, etäisyys, asento, tähtäysaika, kokemus ja lamautus.
- **Säde tarkistaa** osumat esteisiin ja kohteisiin. Suojan arvo ratkaisee, pysähtyykö luoti.
- **Osuman vaikutus** ratkaistaan taulukosta: lievä, vakava (verenvuoto), toimintakyvytön tai kuollut. Lääkintämies voi hoitaa.
- **Sarjatuli** on useita erillisiä säteitä.

### 8.2 Lamautus

- Lähelle menevät luodit (2–3 m) ja räjähdykset kasvattavat lamautusta. Voimakkuus riippuu aseesta.
- Lamautus hälvenee ajan myötä, nopeammin suojassa ja johtajan lähellä.
- Rajat: lamautettu-tila ja moraalitesti.

### 8.3 Moraali

- **Laukaisevat tapahtumat:** toveri kaatuu, johtaja kaatuu, panssarivaunu ilmestyy, tulta sivusta tai takaa, suuri lamautus.
- **Muokkaajat:** johtaja, kokemus, suoja, väsymys ja ryhmän tappiot.
- **Tilat** kuten kohdassa 6.4.

### 8.4 Lähitaistelu

- Alkaa noin 2 m etäisyydellä.
- Ratkaisu on vastakkainen heitto, johon vaikuttavat ase, taito, yllätys, moraali ja haavat. Kesto muutama sekunti.
- Murtunut sotilas pakenee tai antautuu. Antautuneista tulee vankeja.

### 8.5 Kranaatit ja räjähdykset

- Heitto kaarena, laskeutuminen hajonnalla ja räjähdys sytyttimen viiveen jälkeen.
- Sirpaleiden vaikutus lasketaan säteinä räjähdyspisteestä. Suoja ja asento vähentävät vaikutusta.

### 8.6 Panssarit ja ajoneuvot

- **Panssari suunnan mukaan:** edessä, sivulla, takana ja päällä on omat paksuudet ja kaltevuudet.
- **Läpäisy** riippuu ammuksesta ja etäisyydestä. Osumakohta arvotaan taulukosta: runko, torni tai tela.
- **Vaikutukset:** ei läpäisyä (tärähdys), miehistön jäsen haavoittuu, liikuntakyvytön, tykki rikki, tuhottu tai palaa.
- **Miehistö:** ajaja, ampuja ja johtaja. Luukut kiinni heikentävät näkyvyyttä, luukut auki altistavat johtajan.
- **Katvealue** vaunun lähellä.
- **Jalkaväen keinot:** Lahti L-39, polttopullo ja kasapanos.
- **Ajoneuvot yleensä** ovat yksiköitä, joilla on liikkumistapa ja miehistöpaikat. Samalla mallilla toimivat myöhemmin myös kuorma-autot ja hevoset.

### 8.7 Epäsuora tuli

1. **Tilaus** radiolla: kestää aikaa, ja yhteys voi epäonnistua.
2. **Viive** ennen ensimmäistä iskua: kranaatinheittimellä 60–120 s.
3. **Tähystyslaukaus** ja korjaukset, joiden jälkeen virhe pienenee.
4. **Tehokas tuli:** sovittu määrä kranaatteja kohdealueelle.
5. **Rajat:** ammukset rajallisia tehtävää kohden. Savua voi käyttää. Liian lähelle omia tilattu tuli voi osua omiin.

### 8.8 Data

Aseet, ajoneuvot ja yksiköt ovat YAML-tiedostoja. Esimerkki:

```yaml
id: suomi_kp31
name: "Suomi KP/-31"
type: smg
magazine: 71
fire_modes:
  burst: { rounds: 5, aim_ticks: 6, recover_ticks: 6 }
accuracy: { base_spread_deg: 2.5, range_falloff_m: 60 }
damage: { lethality: 0.55 }
suppression: { per_round: 0.8, radius_m: 2.0 }
melee_bonus: 0.3
noise_db: 95
```

## 9. Vihollisen tekoäly

### 9.1 Periaate

Vihollisen sotilaat käyttävät samaa tekoälyä kuin pelaajan miehet (kohta 6.7). Niiden päällä on komentaja. Tekoäly ei huijaa: se käyttää vain oman osapuolensa havaintoja.

### 9.2 Valppaus

| Tila | Käyttäytyminen |
|---|---|
| Rauhallinen | vartiointi, partio, hidas havaitseminen |
| Epäluuloinen | kuuntelee, katsoo, voi tutkia, palaa rauhalliseksi jos mitään ei tapahdu |
| Hälytetty | menee asemiin, herättää muut |
| Taistelu | täysi taistelukäyttäytyminen |

### 9.3 Hälytyksen leviäminen

- **Huuto:** noin 50 m
- **Laukaus:** kuuloetäisyys
- **Valoraketti:** näkyy kauas ja valaisee
- **Kenttäpuhelin:** käynnistää tehtävän tapahtumat, esim. vastaiskun

Pelaaja voi hidastaa leviämistä hiljaisella taistelulla tai katkaisemalla puhelinjohdon.

### 9.4 Komentaja

- **Tehtävät:** puolusta, vahvista, vastaisku, koukkaa, tilaa tulta, vetäydy.
- **Vaikutuskartta:** 10 m ruudukko, johon lasketaan tunnettu ja epäilty vihollinen, vaara-alueet, suoja ja tehtävän kannalta tärkeät alueet.
- **Päätöksentekoväli:** 2–5 s.

### 9.5 Tehtävän tekijän työkalut

- partioreitit ja vartiopaikat
- alueet, kuten puolustettava asema, kokoontumispaikka ja vetäytymisreitti
- suunnitelmat (tapahtumat)
- luonne: aggressiivisuus, kokemus ja reaktioaika

## 10. Modattavuus

### 10.1 Paketit

Kaikki sisältö, myös perussisältö, on paketeissa. Paketti voi lisätä uutta tai korvata olemassa olevaa samalla tunnisteella. Latausjärjestys määritellään `pack.yaml`-tiedostossa.

```
content/core/
├─ pack.yaml
├─ weapons/  units/  vehicles/  tilesets/
└─ missions/iskuosasto/
   ├─ mission.yaml
   ├─ map.tmx
   ├─ briefing.md
   ├─ script.lua        # valinnainen
   └─ assets/
```

### 10.2 Kartat Tiledissa

Kartan kerrokset ovat maasto, korkeus, esineet ja tekoäly. Esineillä (kivet, puut, juoksuhaudat, korsut, piikkilanka) on valmiit arvot tiilisarjassa.

### 10.3 Tehtävän kuvaus

`mission.yaml` määrittelee:

- vuorokaudenajan ja aikamuodot
- joukot ja tuen
- tavoitteet
- tapahtumat muodossa "kun … niin …"

Esimerkki:

```yaml
id: iskuosasto
title: { fi: "Iskuosasto", en: "Strike Detachment" }
time_of_day: dawn
default_mode: realtime_pause
contact_mode: turn_based

forces:
  player:
    - { squad: fin_strike_squad, spawn: start_zone }
    - { support: fin_mortar_81, ammo: { he: 24, smoke: 8 } }
  enemy:
    - { squad: sov_outpost_guard, zone: outpost, patrol: path_north }
    - { squad: sov_outpost_sleeping, zone: bunker_1, alertness: relaxed }

objectives:
  - { id: grab_docs, type: pick_up, item: documents, in: bunker_1 }
  - { id: extract, type: reach_zone, zone: start_zone, requires: [grab_docs] }

events:
  - when: { alarm_raised: enemy }
    do:
      - { wait_seconds: 180 }
      - { spawn: sov_counterattack_platoon, at: north_edge, order: counterattack, zone: outpost }
      - { spawn: sov_t26, at: north_road, order: support }
```

### 10.4 Lua

- MoonSharp-hiekkalaatikko: ei pääsyä tiedostoihin, verkkoon tai käyttöjärjestelmään, vain pelin omiin rajapintoihin.
- Satunnaisuus vain simulaation generaattorista.

### 10.5 Työkalut

- **Tarkistin:** `nmf validate <polku>`
- **Kuumalataus** kehitystilassa
- **Kehittäjätila:** koko kartta näkyvissä, tekoälyn aikeet ja näkölinjat piirrettyinä, aika nopeutettavissa
- **Dokumentaatio** ja "Iskuosasto" mallitehtävänä

### 10.6 Lisenssit ja jakaminen

- **Koodi:** MIT. **Grafiikka ja äänet:** CC BY-SA 4.0. **Tehtävät:** tekijä valitsee lisenssin.
- **Jakaminen:** ensin zip-tiedostot tai GitHub. Myöhemmin mahdollisesti itch.io tai Steam Workshop.

## 11. Ensimmäinen tehtävä: "Iskuosasto"

**Paikka ja aika:** Maaselän kannas, asemasota 1942, aamuhämärä, kesä.

**Joukot:**
- **Pelaaja:** 10–12 miestä. Mukana johtaja, nimetyt tarkka-ampuja, pioneeri ja radiomies, kivääriryhmä ja pikakivääri. Tukena 81 mm kranaatinheitin radiolla.
- **Vihollinen:** etuvartioasema, jossa juoksuhautoja, korsu, konekivääripesäke ja vartiomiehiä. Hälytyksen jälkeen vastaisku, jossa T-26.

**Kulku:**
1. soluttautuminen metsän ja suon läpi reaaliajassa
2. kontakti ja siirtyminen vuoropohjaiseen tilaan
3. juoksuhautataistelu
4. asiakirjojen nouto korsusta
5. vetäytyminen vastaiskun alla (savu, tuli, panssarintorjunta)
6. paluu omalle puolelle

## 12. Testaus

- **Yksikkötestit** simulaation säännöille: näkölinja, havaitseminen, äänet, toimintojen kestot, tuli, lamautus, moraali, lähitaistelu, panssarin läpäisy, epäsuora tuli ja aikamuotojen vaihto.
- **Deterministisyystesti:** sama siemen ja käskyjono tuottavat saman lopputilan (tilan tiiviste).
- **Uusintaregressio:** tallennetut uusinnat ajetaan testeissä ja lopputila verrataan.
- **Headless-ajot:** `Nmf.Cli` ajaa tehtävää tekoäly tekoälyä vastaan ja tuottaa tilastoja tasapainotusta varten.
- **Sisällön tarkistus:** `nmf validate` ajetaan kaikelle sisällölle testeissä.

## 13. Toteutuksen vaiheet (karkea järjestys)

1. **Ydin:** projektin rakenne, kiinteä askel, satunnaislukugeneraattori, käskyt ja tapahtumat, kartan solut ja Tiled-lataus.
2. **Liike ja näkyvyys:** reitinhaku, toiminnot, näkölinja, havaitseminen, äänet ja sumu. Godotissa ensimmäinen näkymä, jossa yksiköt liikkuvat kartalla.
3. **Tuli ja moraali:** laukaukset, lamautus, moraali, haavat, johtajat ja komentoketju.
4. **Aikamuodot:** reaaliaika + tauko, vuoropohjainen tila keskeytyksineen ja vaihto niiden välillä.
5. **Tekoäly:** sotilaan hyötyfunktio, johtajan suunnittelija, valppaus ja komentaja.
6. **Lähitaistelu, kranaatit, panssarit ja epäsuora tuli.**
7. **Tehtäväjärjestelmä ja modattavuus:** mission.yaml, tapahtumat, Lua, paketit ja tarkistin.
8. **"Iskuosasto":** kartta, sisältö, tasapaino, paikkagrafiikat ja äänet.

Jokaisesta vaiheesta tehdään oma toteutussuunnitelmansa.

## 14. Avoimet kysymykset

- **Grafiikan lähde:** omat pikselit, avoimet paketit vai yhteistyö piirtäjän kanssa. Ei vaikuta arkkitehtuuriin.
- **Ääninäyttely ja radioviestit:** suomi ja venäjä.
- **Tarkat numeeriset arvot** (kestot, hajonnat, moraalirajat) haetaan testaamalla, ja taulukoiden arvot ovat lähtökohtia.
