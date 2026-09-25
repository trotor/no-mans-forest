# No Man's Forest – taistelumalli, vaihe 3

Päivämäärä: 2026-09-24
Tila: hyväksytty keskustelussa (vaihtoehto A), toteutetaan suoraan
Liittyy: pääspeksi `2026-09-24-no-mans-forest-design.md` §6 ja §8, grafiikka `2026-09-24-pixel-art-design.md`

## 1. Tavoite ja rajaus

**Tavoite:** testikartalla syntyy pelattava tulitaistelu. Sotilaat ampuvat itse näkyviä vihollisia, joutuvat lamautetuiksi, menevät maahan, haavoittuvat, kaatuvat ja murtuvat, ja johtaja pitää ryhmää koossa. Pelaaja ohjaa liikettä, asentoa, tuliperiaatetta ja maalia.

**Mukana:**
- laukausten ballistiikka suojineen
- lamautus
- moraalin tilat (kunnossa, lamautettu, murtunut) ja kokoontuminen
- haavat ja verenvuoto
- johtaja ja johtajan vaihtuminen
- tuliperiaatteet ja itsenäinen maalinvalinta
- kolme asetyyppiä kummallekin puolelle YAML-tiedostoina
- tehosteet: luotien jäljet, suuliekit, osumat ja kaatuneiden hahmot
- käyttöliittymä: tila, kunto, moraali, lamautus ja tuliperiaate

**Rajattu pois** (tulevat myöhemmin):
- käsikranaatit ja lähitaistelu (seuraava vaihe)
- panssarit ja epäsuora tuli
- paniikki ja sankaruus (pääspeksin harvinaiset moraalitapahtumat)
- ryhmäkäskyjen suunnittelija (johtaja jakaa ryhmän tehtävän)
- lääkintämies
- useampi ryhmä per puoli (nyt koko puoli on yksi ryhmä)
- äänet

## 2. Aseet (data)

**Tiedostot:** `content/core/weapons/<id>.yaml`, yksi ase per tiedosto. Kentät:

| Kenttä | Merkitys |
|---|---|
| `id`, `name`, `class` | tunniste, nimi, tyyppi (`rifle` / `smg` / `lmg`) |
| `magazine` | lippaan koko (reservi on rajaton) |
| `aim_ticks` | tähtäyksen kesto |
| `burst` | laukauksia per sarja |
| `round_interval_ticks` | askelia laukausten välillä sarjassa |
| `recover_ticks` | palautuminen sarjan jälkeen (esim. lukon veto) |
| `reload_ticks` | lippaan vaihto |
| `spread_mrad` | hajonta milliradiaaneina (seisten, lamautumattomana) |
| `range_m` | suurin ampumaetäisyys |
| `lethality_pct` | osuman vakavuus (ks. §4) |
| `suppression` | lamautuspisteitä per läheltä mennyt luoti |
| `noise_m` | laukauksen kuuluvuus |

**Aseet**

| Suomi | Neuvostoliitto | Käyttäjä |
|---|---|---|
| Kivääri M/39 (`mosin_m39`) | Kivääri 91/30 (`mosin_9130`) | kiväärimiehet |
| Suomi KP/-31 (`suomi_kp31`) | PPŠ-41 (`ppsh41`) | johtaja |
| Lahti-Saloranta M/26 (`lahti_saloranta`) | DP-27 (`dp27`) | toinen mies (pikakivääri) |

**Varustus testitaistelussa:** kunkin puolen ensimmäinen syntypiste on johtaja konepistoolilla, toinen on pikakiväärimies ja loput kiväärimiehiä.

## 3. Laukaus

1. **Tähtäyspiste:** ampuja tähtää maalin vartaloon (maan korkeus + 60 % asennon korkeudesta).
2. **Virhe:** vaaka- ja pystyvirhe arvotaan kumpikin tasaisesti väliltä ±hajonta (mrad). Hajontaa muokkaavat:
   - asento: seisten 100 %, kyykyssä 80 %, makuulla 60 %
   - lamautus: +0…200 % (hajonta × (100 + lamautus/5) %)
3. **Lento:** luoti kulkee ampujan silmistä virheellisen tähtäyspisteen kautta aseen kantamaan asti. Korkeus muuttuu lineaarisesti.
4. **Maasto:** reitin soluissa:
   - jos luodin korkeus on ≤ maan korkeus, luoti pysähtyy (kukkula tai maa)
   - jos luoti on esteen korkeuden alapuolella, se pysähtyy todennäköisyydellä `cover / 255`
5. **Osuma:** ensimmäinen elossa oleva yksikkö lentoradalla, kun
   - sivuetäisyys ≤ 25 cm (makuulla 30 cm), ja
   - luodin korkeus on yksikön maan ja pään välissä.

   Kuka tahansa voi saada osuman, myös oma mies.
6. **Läheltä meno:** vihollispuolen sotilaat, joiden ohi luoti menee alle 2,5 metrin päästä ennen pysähtymistään, saavat lamautusta suhteessa etäisyyteen.
7. **Tuli paljastaa:** ampunut sotilas on seuraavat 2 s neljä kertaa helpompi havaita, ja laukaus kuuluu `noise_m` metrin päähän. Viholliselle tästä tulee kuultu havainto.

## 4. Haavat

Osuman vakavuus arvotaan väliltä 0–99 (L = `lethality_pct`):

| Arvonta | Tulos |
|---|---|
| < L/3 | kuollut |
| < 2L/3 | toimintakyvytön (maassa, ei tee mitään) |
| < L | vakava (nopeus 50 %, vuotaa: toimintakyvytön 90 s kuluttua) |
| muu | lievä (nopeus 85 %) |

- **Uusi osuma** pahentaa haavaa vähintään yhden portaan.
- **Toimintakyvytön ja kuollut** kaatuvat heti makuulle, keskeyttävät kaiken eivätkä tottele käskyjä.

## 5. Lamautus ja moraali

**Lamautus** (0–1000)
- **Kasvaa:**
  - läheltä mennyt luoti: aseen `suppression`-arvo × (1 – etäisyys / 2,5 m) (myös luoti, joka iskeytyy suojaan alle 2,5 m päähän hänestä)
  - osuma: +250
- **Hälvenee** 20 pistettä sekunnissa (tasaisesti jaettuna askelille). Makuulla hälvenee 10 / s enemmän, ja johtajan komentoalueella vielä 10 / s enemmän. Lamautettu mies pysyy siis maassa useita sekunteja tulen loputtua, ja jatkuva tuli pitää hänet siellä.

**Tilat**
- **Kunnossa:** normaali.
- **Lamautettu:** lamautus ≥ 400, ja tila palautuu, kun lamautus laskee alle 250.
  - menee makuulle
  - ei liiku (liikekäskyt hylätään)
  - tähtää 1,5 kertaa hitaammin, mutta voi ampua
- **Murtunut:** moraalitestin tulos.
  - ei tottele
  - juoksee noin 20 m poispäin tunnetuimmasta vihollisesta
  - menee siellä makuulle eikä ammu

**Moraali**
- **Arvot:** 0–1000. Perustaso on 700, johtajalla 800.
- **Palautuu** hitaasti (+5 / s), kun lamautus on alle 100.
- **Menetykset:**
  - oma haava: –100
  - toveri kaatuu tai tulee toimintakyvyttömäksi 20 m säteellä: –60
  - johtaja kaatuu: koko puoli –150

**Moraalitesti**
- **Milloin:** kun lamautus ylittää 800, kun sotilas haavoittuu, kun toveri kaatuu lähellä ja kun johtaja kaatuu.
- **Kaava:** arvonta 0–999 ≥ moraali + johtajabonus – lamautus/4 – haavarangaistus → murtuu.
  - johtajabonus: 150 × johtajan laatu
  - haavarangaistus: lievä 100, vakava 250

**Kokoontuminen:** murtunut yrittää koota itsensä sekunnin välein. Onnistumisen todennäköisyys on (moraali + kokoamisbonus – lamautus) / 1000:
- johtaja 30 metrin säteellä ja kunnossa: kokoamisbonus 200 × johtajan laatu
- ilman johtajaa: –300

Onnistuessaan sotilas palaa kuntoon tai lamautetuksi lamautuksen mukaan, ja moraali nousee 100.

**Johtaja**
- **Komentoalue:** 30 m.
- **Johtajan kaatuessa** pienimmän tunnisteen elossa oleva toimintakykyinen mies ottaa johdon laadulla 50 %.

## 6. Itsenäinen toiminta ja käskyt

**Tuliperiaate** (oletus: ammu vapaasti)
- **Ammu vapaasti:** ampuu lähintä vihollista, joka on puolen tiedossa näkyvänä, jonka sotilas näkee itse ja joka on kantaman sisällä.
- **Vain vastatuli:** kuten edellä, mutta vain vihollisia, jotka ovat ampuneet viimeisen 10 sekunnin aikana.
- **Älä ammu:** ei ammu.

**Itsenäiset päätökset** (joka viides askel, näkyvyyden päivityksen jälkeen)
- menee makuulle, kun lamautus on ≥ 250 eikä se ole liikkeessä
- valitsee maalin, kun se on vapaa (ei liikettä, asennon vaihtoa eikä toimintoa kesken)
- murtuneena perääntyy

**Ampuminen**
- **Vaiheet:** tähtäys → sarja (laukaukset välein) → palautuminen, tai lippaan vaihto kun lipas on tyhjä. Kaikki ovat askelten mittaisia toimintoja.
- **Keskeytys:** liike, asennon vaihto, maalin katoaminen näkyvistä tai kaatuminen keskeyttää tähtäyksen ja sarjan.

**Käskyt**
- **Uudet käskyt:**
  - `FireAtOrder(sotilas, maali)`: suosittu maali, jota käytetään kun se näkyy
  - `SetFirePolicyOrder(sotilas, tuliperiaate)`
- **Hylkäykset:**
  - toimintakyvyttömän tai kuolleen sotilaan käskyt: "unit is out of action"
  - murtuneen sotilaan käskyt: "unit is broken"
  - lamautetun sotilaan liikekäskyt: "unit is pinned"

## 7. Simulaation askel

Askeleen järjestys:
1. käskyt
2. liike
3. tulitoiminta
4. verenvuoto
5. lamautus ja moraali
6. joka viides askel näkyvyys ja kuulo
7. joka viides askel itsenäiset päätökset

**Deterministisyys:**
- kaikki satunnaisuus tulee `Simulation.Rng`:stä
- sotilaat käsitellään tunnistejärjestyksessä
- tilan tiiviste kattaa koko taistelutilan

**Näkyvyys:** kuolleet eivät havaitse, eikä kuolleiden havaintotilaa päivitetä. Näkyvänä kuollut jää näkyväksi ruumiiksi.

## 8. Grafiikka ja käyttöliittymä

**Tehosteet**
- **Luodin jälki:** kirkas viiva suusta pysähtymispisteeseen, 0,12 s.
- **Suuliekki:** 0,06 s.
- **Pölypöllähdys** pysähtymispisteessä, jos luoti ei osunut: 0,35 s.

**Kaatuneet**
- `dead`-animaatio (1 ruutu × 8 suuntaa), joka on valinnainen: sen puuttuessa käytetään makuuhahmoa.
- Ruumiin alle piirretään veritahra.
- Toimintakyvytön näytetään kaatuneen hahmona ilman veritahraa.

**Hahmojen yläpuolella** (vain omat): lamautettu oranssi "!", murtunut punainen "!!" ja lippaan vaihto "R". Valitun sotilaan maali näytetään ohuena punaisena katkoviivana.

**Kortit**
- **Tila:** esim. Firing, Pinned, Broken, Down, Dead.
- **Kunto:** Unhurt, Light wound, Serious wound, Down, Dead.
- **Palkit:** moraali (vihreä) ja lamautus (oranssi).
- **Tuliperiaate** tekstinä.
- **Kaatunut** näytetään harmaana.

**Hiiri ja näppäimet**
- oikea klikkaus näkyvän vihollisen päällä: ampukaa tuota
- P: tuliperiaate kiertää valituille
- yläpalkissa omat ja nähdyt vihollisten tappiot

**Taide:** generaattori saa `dead`-asennon ja `blood`-tahran. Ulkopuolisten kuvien ohje ja kokoamistyökalu päivitetään.

## 9. Testaus

- **Nmf.Sim:** ballistiikka (suoja, kukkula, osuma ja ohi, läheltä meno, oma tuli), haavat ja pahentuminen, verenvuoto, lamautuksen kasvu ja hälveneminen, tilasiirtymät, moraalitesti, kokoontuminen, johtajan vaihtuminen, tuliperiaatteet, maalinvalinta, murtuneen perääntyminen, käskyjen hylkäykset ja deterministinen kokonainen tulitaistelu.
- **Nmf.Content:** aseiden YAML (kelvollinen, puuttuva kenttä, virheellinen arvo) ja oikean testikartan tulitaistelu ilman poikkeuksia.
- **Nmf.Client:** tehosteiden elinkaari, tilatekstit, tulikäskyt ja vihollisen poiminta klikkauksesta.
- **Python:** `dead`-rivi ja `blood`-tahra.
- **Godot:** käännös, savutesti ilman ikkunaa ja kuvakaappaus käynnissä olevasta tulitaistelusta.
