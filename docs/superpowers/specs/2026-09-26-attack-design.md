# No Man's Forest – hyökkäys tulella ja liikkeellä (vaihe 4e)

Päivämäärä: 2026-09-26
Tila: käyttäjän pyyntö ("tuplaklikkaus tiettyyn viholliseen aloittaa kyseistä vihollista kohti hyökkäyksen. Ukot itse suunnittelevat parhaan mahdollisen tavan, eli maahan ja ampumaan tms."), toteutetaan suoraan
Liittyy: kranaatit ja rynnäkkö `2026-09-24-grenades-melee-design.md` §5, suojautuminen `2026-09-25-take-cover-design.md`

## 1. Ohjaus

| Toiminto | Mitä tapahtuu |
|---|---|
| Tuplaklikkaus nähtyyn viholliseen | hyökkäys (`AttackOrder`) kaikille komennossa oleville |
| Shift + tuplaklikkaus | entinen suora rynnäkkö (`AssaultOrder`) |
| Uusi liike-, pysähdys-, asento-, rynnäkkö- tai ryöstökäsky | mies irtoaa hyökkäyksestä |

## 2. Hyökkäysryhmä

**Muodostuminen:** saman puolen samaa kohdetta vastaan samalla askeleella annetut hyökkäyskäskyt muodostavat yhden ryhmän.

**Puoliskot:** jäsenet järjestetään tunnisteen mukaan, ja ne jaetaan vuorotellen puoliskoihin A ja B.

**Ryhmän päätökset** tehdään joka 5. askel ennen miesten omia päätöksiä, tässä järjestyksessä:
1. **Jäsenten karsinta:** toimintakyvyttömät, murtuneet ja muun käskyn saaneet jäsenet poistuvat.
2. **Kohde kaatunut** (toimintakyvytön tai vanki): hyökkäys päättyy, ja miehet jäävät paikoilleen.
3. **Kohteen paikka:**
   - nähty: todellinen paikka
   - viimeksi nähty tai kuultu: puolen tieto
   - muuten: ryhmän viimeksi tallentama paikka
   - jos paikkaa ei ole lainkaan: hyökkäys päättyy
4. **Loppurynnäkkö:** alkaa, kun lähin jäsen on alle 25 m päässä ja kohde on lamautettu tai lamautus on vähintään 250, tai kun matkaa on alle 15 m. Kaikki jäsenet saavat rynnäkön (kranaatit ja lähitaistelu, vaihe 3b), ja ryhmä purkautuu.
5. **Eteneminen vuorotellen:**
   - **Ryntäävä puolisko** (aluksi A): jokainen jäsen juoksee noin 25 m kohdetta kohti. Pituus on kuitenkin korkeintaan niin, että hän pysähtyy noin 20 m päähän kohteesta. Hän hakeutuu siellä lähimpään suojaan uhkaa eli kohdetta vasten (suojanhaku 8 m säteellä). Jos suojaa ei ole, hän pysähtyy pisteeseen. Perillä hän ottaa ampuma-asennon.
   - **Tukeva puolisko:** pysyy paikallaan ampuma-asennossa kohdetta vasten ja ampuu kohdetta, kun näkee sen. Käskyn maali on kohde.
   - **Vaihto:** kun kaikki ryntääjät ovat perillä tai 15 s on kulunut, puoliskot vaihtavat vuoroa. Jos toinen puolisko on tyhjä, jäljellä olevat etenevät vuorotellen yksin.
6. **Hyökkääjät eivät reagoi tulen avaukseen** (vaihe 3d), koska suunnitelma käyttää jo suojaa. Lamautus pysäyttää tavalliseen tapaan.

**Tilat korteissa:** "Bounding" (ryntää) ja "Covering fire" (tukee tulella).

## 3. Testaus

- **Muodostus:**
  - yksi ryhmä
  - vuorottaiset puoliskot
  - aluksi A ryntää ja B tukee (B:n maali on kohde)
- **Ryntäyspiste:** lähempänä kohdetta, mutta ei alle noin 20 m.
- **Vuoronvaihto:** kun ryntääjät ovat perillä, sekä aikakatkaisulla.
- **Loppurynnäkkö:** lähellä ja lamautettu kohde → rynnäkkö.
- **Päättyminen:** kohde kaatuu → hyökkäys päättyy.
- **Muu käsky** irrottaa miehen ryhmästä.
- **Hylkäykset:** virheellinen kohde, lamautettu mies.
- **Oikea kartta:** suomalaiset hyökkäävät Belovia kohti. Tulos on deterministinen, suomalaiset etenevät ja ampuvat.
- **Client:** tuplaklikkaus → hyökkäys, Shift + tuplaklikkaus → rynnäkkö. Kortin tilat.
