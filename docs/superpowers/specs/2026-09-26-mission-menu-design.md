# No Man's Forest – tehtävävalikko, tehtävän valmistuminen ja suoritukset (vaihe 4m)

Päivämäärä: 2026-09-26
Tila: käyttäjän pyyntö ("toteuta myös tehtävän valmistuminen ja tehtävien hallinta/valinta; toki tässä vaiheessa on vain yksi tehtävä"), toteutetaan suoraan
Liittyy: tehtävät `2026-09-26-missions-design.md`

## 1. Tehtäväluettelo

`MissionLoader.LoadAll(content/core/missions)` palauttaa jokaisen kansion, jossa on `mission.yaml`, kansion nimen mukaisessa järjestyksessä. Rikkinäinen tehtävä tulee mukaan virheilmoituksineen eikä kaada luetteloa.

## 2. Tulos ja suoritukset

- **Tulos** (`MissionResult`), kun tehtävä päättyy:
  - onnistui vai epäonnistui
  - peliaika
  - omat kaatuneet ja haavoittuneet
  - nähdyt vihollisen tappiot
  - tavoitteet tehty / kaikki
- **Suoritukset** (`MissionProgress`, `user://progress.json`) tehtävittäin:
  - yritysten määrä
  - suoritettu vai ei
  - paras onnistuminen (vähiten kaatuneita, sitten haavoittuneita, sitten nopein)
  - viimeisin tulos
- **Tallennus:** rikkinäinen tai puuttuva tiedosto on puhdas alku. Demot ja kuvakaappaukset eivät tallenna.

## 3. Näkymät

- **Tehtävävalikko:** peli avautuu siihen, ellei komentorivi pyydä tehtävää, karttaa, demoa, kuvakaappausta tai paperia.
  - Valikossa on otsikko ja luettelo, jossa jokaisella tehtävällä on nimi, aika ja paikka sekä tila: "Uusi tehtävä", "2 yritystä — ei vielä suoritettu" tai "✓ Suoritettu — paras: 1 kaatunut, 2 haavoittunutta, 12:34".
  - Lisäksi on kielen valinta (Suomi / English) ja painike Lopeta.
  - `--menu` pakottaa valikon.
- **Tehtävän päätyttyä** tulospaneelissa on otsikko ja tulos sekä painikkeet Jatka katselua, Yritä uudelleen ja Tehtävävalikko.
- **Pelin aikainen valikko** avautuu yläpalkin painikkeesta Valikko tai näppäimellä Esc, kun mitään ei ole valittuna.
  - Painikkeet: Jatka, Aloita alusta, Tehtävävalikko ja Lopeta peli.
  - Peli on tauolla valikon ollessa auki.
- **Käynnistys:** tehtävä käynnistetään lataamalla näkymä uudelleen (`ReloadCurrentScene`). Valinta ja kieli säilyvät staattisessa tilassa latauksen yli.

## 4. Testaus

- **Content:** luettelo, rikkinäinen tehtävä ja puuttuva kansio; ydintehtävissä on Iskuosasto.
- **Client:**
  - tulos lasketaan istunnosta
  - yritykset ja paras onnistuminen
  - JSON meno–paluu ja rikkinäinen tiedosto
  - tilatekstit suomeksi ja englanniksi
- **Godot:** käännös ja kuvakaappaus valikosta (`--menu`).
