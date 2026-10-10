# Talvi, valo ja karttaputki (vaihe 8e, skenaarioiden eepos)

Eepos 3 (spec 2026-10-09-roadmap-epics) vaihe 5: lumimaasto ja sen grafiikka, upottava lumi, sukset ja jäljet lumessa
pelimekaniikkana sekä mapgenin yleistys. Reunaehtoina ovat roadmapin säännöt: talvi kumoaa pääspecin §1.4:n, valo
vaikuttaa havaitsemiseen hämärässä ja yöllä, ja maankäyttö otetaan kesäkuvista ja lumi piirretään päälle.

Käyttäjä 10.10.2026: "tee päätöksiä ja ala toteuttaa".

## 1. Periaate

- **Talvi on tehtävän asetus, ei kartan.**

  ```yaml
  winter: { snow_cm: 40 }
  light: dusk                                   # day (oletus), dusk tai night
  ```

  Sama kesäkartta käy talveen. Metsä, aukea ja suo säilyvät, puut ja maastotyypit pysyvät, ja lumi on kaikkialla
  yhtä syvää.
- **Simulaatio:** talvi ja valo ovat `Simulation.Winter` ja `Simulation.LightPct`. Ne ovat mukana `StateHash`issa
  ja asetetaan tehtävän luonnissa.
- **Vanhat tehtävät eivät muutu:** ilman `winter`- ja `light`-asetusta yksikään uusi sääntö ei koske tehtävää.
  Mittarin tulos pysyy merkki merkiltä samana.

## 2. Lumi ja liike

- **Jalan** lumi hidastaa: solun kustannus kerrotaan lumikertoimella `100 + 1,25 × snow_cm` % (40 cm: 150 %).
  Lumikerroin on enintään 250 %. Esimerkiksi metsän 130 % on lumessa 195 %.
- **Tiellä** lumi on tallattua, joten jalankulkija ei hidastu.
- **Suksilla** (`skis: true` sotilaalle) kävely ja juoksu kulkevat aukealla, suolla ja tiellä 70 % kesäisestä
  solukustannuksesta ja metsässä 85 %:sta, eikä lumi hidasta. Hiipiessä ja ryömiessä sukset eivät auta: silloin
  mennään jalan.
- **Reitinhaku** ei tunne lunta eikä suksia, koska kerroin on lähes tasainen. Päätös: hyväksytään.
- **Kuuluminen:** liikkeen ääni ei muutu lumessa.
- **Ilman talvea** `skis` ja `ahkio` eivät tee mitään. Lataaja hyväksyy ne, koska ne ovat varusteita, eivät virheitä.
- **Ahkio** (`ahkio: true` sotilaalle): talvella hänen kantamansa mies on ahkiossa. Vauhti on kävellen 80 % ja
  juosten 90 %, avustettuna 90 % ja 95 % (kantaen 55/80 ja 75/90). Kädet ovat varattuina kuten kantaessa.
- **Järvet** pysyvät kulkukelvottomina. Jäälle meno tulee myöhemmin (päätös).

## 3. Jäljet

- Talvella jokainen liikkuva mies jättää jäljen jokaiseen soluun, jonka hän ylittää. Jäljet ovat simulaation tilaa
  (`Simulation.Tracks`, mukana `StateHash`issa), ja jokaisesta solusta muistetaan viimeisin kulkija: puoli ja tick.
- **Vihollinen huomaa jäljet:** rauhallinen mies, joka näkee vastapuolen tuoreet jäljet (alle 30 min) 20 m:n
  sisällä, muuttuu epäluuloiseksi. Hälytyspiste on tuorein näkemistä jälkisoluista.
- **Etsintäpartio seuraa jälkiä:** jos joku partiosta näkee vastapuolen jälkiä 25 m:n sisällä, partio menee
  tuoreimpaan niistä ruudukon sijaan. Näin se seuraa jälkeä kohti tuoreempaa.
- **Pelaaja näkee:**
  - omat jäljet aina
  - vihollisen jäljet vain siellä, missä sumu on nyt auki (pelaajan miehet näkevät)
  - jäljet karttanäkymässä pieninä painaumina

## 4. Valo

| `light` | Havaitseminen | Lumen kanssa |
|---|---|---|
| day | 100 % | |
| dusk | 55 % | 70 % |
| night | 25 % | 40 % |

- Valo kerrotaan havaitsemiseen kaikille. Lumi heijastaa valoa, joten hämärä ja yö ovat lumen kanssa valoisampia.
- **Valoraketti valaisee:** 20 sekunnin ajan 200 m:n säteellä raketista valo on 100 %. Valo katsotaan
  havaittavan miehen kohdalta: valaistu mies näkyy, mutta valossa seisova ei näe pimeään paremmin. Raketit ovat simulaation
  tilaa, ja ne näkyvät pelaajalle (vaihe 8d).
- **Pelaajalle:** ruutu himmenee valon mukaan, ja raketti valaisee piirrossa ympäristönsä.

## 5. Grafiikka ja ääni

- **Maa:** lumi sekoitetaan maaston päälle shaderissa (`snow`, 0–1). Metsän alla on harvempaa lunta, ja tie näkyy
  tallattuna. Vesi pysyy vetenä.
- **Koristeet:** talvella maan pikkukasvit (sanikat, kukat, sara ja niin edelleen) puuttuvat. Puiden latvat
  vaalenevat kuin huurteessa.
- **Ahkio:** kantajan alla piirretään reki.
- **Sukset:** kortin tilarivissä lukee *Sukset*.
- **Äänet:** `footstep.snow`-ääni talvella. Tiedostoja ei vielä ole.

## 6. Karttaputki

`tools/mapgen` yleistetään niin, että jokaiselle skenaariolle saa oman alueen (`areas.py`):
- järvi on valinnainen (`lake_relation` ja `lake_seed`)
- aukeiden osuus (`open_share`) ja siemen (`seed`) annetaan alueen mukaan
- joukkojen, poteroiden ja vyöhykkeiden sijoitus on valinnainen (`forces: false`)
- komentorivi vaatii alueen nimen

Uutta aluetta ei haeta tässä vaiheessa, vaan hylkytehtävän kartta tehdään vaiheessa 8f.

## 7. Sisältö

Kehitystehtävä `talvikoe` (debug) karhumäen kartalla:
- 40 cm lunta ja hämärä
- kolme hiihtäjää ja yksi ahkiomies
- tavoitteena tuoda haavoittunut takaisin
- partio, joka voi löytää jäljet

## 8. Testit

- `WinterTests`:
  - lumi hidastaa jalkamiestä
  - tie ei hidasta
  - sukset nopeuttavat, paitsi hiivittäessä
  - ahkio on kantamista nopeampi
  - kesä ei muutu
- `TrackTests`:
  - jäljet syntyvät vain talvella
  - rauhallinen vartija huomaa tuoreet jäljet
  - vanhat jäljet eivät herätä
  - etsintäpartio seuraa jälkiä
  - sama siemen antaa saman tuloksen
- `LightTests`:
  - hämärä hidastaa havaitsemista
  - lumi valaisee
  - raketti valaisee 200 m:n säteellä 20 s
- `MissionLoaderTests`: `winter`, `light`, `skis` ja `ahkio`.
- Client:
  - jälkien näkyvyys sumun mukaan
  - kortin *Sukset*-rivi
  - valon himmennys
- Python: mapgen-alue ilman järveä ja joukkoja.

## 9. Mittaus (10.10.2026)

`probe-attack --mission iskuosasto --tactic suggest --seeds 24 --minutes 8 --winter 40` verrattuna kesään:

| | Talvi | Kesä |
|---|---|---|
| Hyökkääjiä menetetty | 4,3 | 4,4 |
| Puolustajia kaatunut | 1,7 | 3,5 |
| Vihollisjohtaja kaatui | 3 / 24 | 9 / 24 |

Hyökkäykset eivät pysähdy hitauteen: pysähtymissyiden jakauma on sama tai pienempi, ja aikarajat venyvät lumen mukana.
Lumessa hyökkääminen on selvästi vaikeampaa, mikä on odotettua. Talvitehtävän tasapaino tehdään vaiheessa 8f.

Kehitystehtävä `talvikoe` toimii ilman näyttöä:
- haavoittunut löytyy ja tuodaan ahkiolla kotiin (tehtävä onnistuu 516 s:ssa)
- vihollisen hiihtopartio lähtee etsimään
- vartio hälyttyy ja ampuu valoraketin

## 10. Loppukatselmoinnin päätökset (10.10.2026)

**Korjattu:**
- `fetch_dem` rikkoi korkeusruudukon, koska silmukkamuuttuja peitti ruudukon koon. Tämä olisi rikkonut seuraavan
  karttahaun. Nyt sille on testi ilman verkkoa.
- Kesän etsintäpartio aloitti toisen kierroksen alueen keskeltä eikä sieltä, missä se oli. Vanha järjestys on
  palautettu, ja sille on testi.
- Talviliike perustuu nyt kesän vauhtiin:
  - tie ei ole talvella kesää nopeampi millään liiketavalla
  - lumi hidastaa myös hiipijää ja ryömijää
  - ryömijä voi syvässä lumessa jäädä joltakin askeleelta paikalleen, mutta on silti liikkeellä (näkyy ja kuuluu)
- Jäljet pidetään puolittain. Sumussa pelaajan jälkien yli kulkeva vihollinen ei enää tee niihin aukkoa eikä
  paljasta itseään.
- Jäljet piirretään vain ruudulla näkyvältä alueelta.
- Valoraketin valo piirretään 10 m:n ruudun tarkkuudella, ja sen säde vastaa simulaation 200 m:ä.

**Lykätty (pieni):**
- Huurreshaderi vaalentaa myös puiden ja kivien varjot.
- `DecorationView`- ja `UnitView`-tiedostoissa on kaksi väärään kohtaan siirtynyttä kommenttia.
- Etsintäpartio ei katso jälkiä matkalla, vain pysähtyessään.
- Suksilla kantava, jolla ei ole ahkiota, saa suksien edun.
- `_trail` vain kasvaa, joten seurattuaan jäljen loppuun partio ei käänny vanhemmalle tuoreelle jäljelle.
