# Soluttautuminen, tehtävien taktiikat ja vihollisen suunnittelija (vaihe 6c)

Taktiikkaeepoksen runko on spec 2026-10-10-tactics-design (kohta "6c"). Vaihe jaetaan kahteen haaraan:

- **6c-1:** soluttautuminen, miesten ominaisuudet ehdotuksessa sekä tehtävän suosimat ja kieltämät taktiikat.
- **6c-2:** vihollinen käyttää suunnittelijaa vastahyökkäyksissä, vanha hyökkäys (`AttackOrder`, `AttackPlanner`,
  `AttackGroup`, `CounterattackOrder`) poistetaan, ja tasapaino mitataan molempiin suuntiin.

## 6c-1

### 1. Soluttautuminen (`content/core/tactics/15_infiltration.yaml`)

```yaml
id: infiltration
name: { en: "Infiltration", fi: "Soluttautuminen" }
infiltrate:
  form_up: { min_m: 18, max_m: 30 }   # hiipivä mies kuuluu 15 metriin: kokoontumispaikka sen ulkopuolella
  min_experience: 60                  # ehdotetaan vain kokeneille miehille
abort: { casualties_pct: 30, pinned_s: 10 }
```

- **Ei tuliasemaa.** Koko ryhmä on iskuryhmä. `TacticPlan.FireTeam` on tyhjä, ja käskyn antaa
  `TacticPlan.FirstMan`.
- **Kokoontumispaikka** on 18–30 m päässä asemasta paikassa, jota asema ei näe (`ThreatMap.SeenExactly`).
  Kulmaehtoa ei ole, koska tulilinjaa ei ole.
- **Reitti** minimoi havaituksi tulemisen:
  - näkyvä solu maksaa kolminkertaisesti tuli ja liike -reittiin verrattuna
  - solu, joka on hiipivän miehen kuuluvuuden sisällä (`VisionRules.NoiseRadiusCm(Sneak)`, 15 m) jostakin
    tunnetusta vihollisesta, maksaa lisää
  - epäillyt ("kuultu") viholliset ovat uhkakartassa kuten ennenkin.
- **Toteutus** (`PlannedAttackRunner`):
  - **Deploy:** kaikki hiipivät (`MoveMode.Sneak`) reittiä pitkin omille paikoilleen. Tulenavauskielto on voimassa,
    jotta kukaan ei paljasta ryhmää, ja miesten oma tulitoiminta palautetaan, kun hyökkäys alkaa tai päättyy.
  - **Paljastuminen:** jos joku joutuu tulen alle ennen kuin kaikki ovat perillä, ryhmä rynnäköi heti, kun sen kärki
    on enintään 40 m päässä asemasta. Kauempana hyökkäys keskeytyy syystä *Paljastui* (`HaltReason.Discovered`), ja
    pelaaja päättää jatkosta.
  - **Assault:** kun kaikki ovat perillä tai hiipimisen aikaraja (4 min) on täynnä, ne, joilla on kranaatti ja jotka
    voivat heittää tunnettuun mieheen, heittävät ensin. Kranaatti lennätetään suojan yli sinne, missä miehen
    *uskotaan* olevan, ja heitot jaetaan aseman miesten kesken. Kaikki rynnäköivät vasta, kun viimeinen
    avauskranaatti on räjähtänyt, ja rynnäkkö heittää lisää kranaatteja matkalla kuten ennenkin. Lamautusta ei
    odoteta.
  - Jos pelaaja vaihtaa miehen tulitoimintaa kesken hiipimisen, valinta pysyy. Uusi hyökkäyskäsky kesken hiipimisen
    palauttaa ensin miehen oman tulitoiminnan.
  - Esikatselu varoittaa, kun miehiä on tulen alla tai he ovat liian kokemattomia.
  - Keskeytykset tappioista, johtajan kaatumisesta ja lamautumisesta toimivat kuten muissa taktiikoissa.
- **Esikatselu:** soluttautumisessa ei ole tuliaseman kahvaa, mutta kokoontumispaikkaa ja reittiä voi raahata.
  Perustelu: *Koko ryhmä (6) hiipii piilossa kokoontumispaikkaan 24 m päähän ja rynnäköi kranaatein.*

### 2. Miehet ehdotuksessa

`TacticPlanner.Suggest` käy taktiikat järjestyksessä ja valitsee ensimmäisen, joka ei ole riskialtis.

- **Tuli ja liike** on riskialtis, kun vihollisia tiedetään vähintään yhtä monta kuin meitä tai kun tuliasema ei
  näe kohdetta (kuten ennen).
- **Soluttautuminen** on riskialtis, kun:
  - reitillä tai kokoontumispaikassa on yksikin kohta, jonka vihollinen näkee
  - miesten keskimääräinen kokemus on alle `min_experience`
  - vihollisia tiedetään vähintään yhtä monta kuin meitä
  - joku miehistä on ollut tulen alla viimeisen 10 sekunnin aikana (kesken tulitaistelun ei hiivitä; lisätty
    mittauksen jälkeen).

Tiedostojärjestys on tuli ja liike (10), soluttautuminen (15) ja pitkä tuli (20). Kun tuliasema ei näe poteroissa
olevia miehiä (6a:n jäljelle jäänyt ongelma), kokeneet miehet hiipivät lähelle. Muut ampuvat pitkää tulta.

Ampujien valinta tuliasemaan pysyy ennallaan: konekivääri ja parhaat ampujat ampuvat, konepistoolimiehet menevät.

### 3. Tehtävän taktiikat (`mission.yaml`)

```yaml
tactics: { prefer: [infiltration], forbid: [long_fire] }
```

- **forbid:** kielletyt poistetaan pelaajan taktiikoista. Ne eivät näy esikatselussa, valikossa eivätkä
  ehdotuksessa.
- **prefer:** suositut tulevat listan alkuun annetussa järjestyksessä, joten ehdotus valitsee niistä ensimmäisen,
  joka ei ole riskialtis.
- Tuntematon taktiikan tunnus on virhe: tehtävä ei käynnisty, ja sisältötesti tarkistaa kaikki tehtävät.
- `iskuosasto` (yöllinen iskuosasto) suosii soluttautumista, jos mittari ei näytä sitä huonommaksi kuin nykyinen
  ehdotus. Tulos kirjataan tähän.

### Mittaukset (10.10.2026)

`probe-attack --mission iskuosasto --seeds 24 --minutes 8`. Mittari antaa hyökkäyskäskyn kesken tulitaistelun.

| Ajo | Suomalaisia poissa | Vihollisia poissa | Johtaja kaatui | Hyökkäyksiä |
|---|---|---|---|---|
| ehdotus, ei suosintaa | 3,5 | 5,9 | 15/24 | 4,6 |
| pelkkä soluttautuminen | 3,1 | 2,6 | 1/24 | 6,1 |
| ehdotus, soluttautuminen suosittuna | 3,7 | 5,6 | 14/24 | 4,6 |

- Ehdotuksen tulos on sama kuin ennen 6c:tä: tässä tehtävässä ehdotus ei valitse soluttautumista.
- Pakotettu soluttautuminen säästää miehiä mutta saa vähän aikaan. 8 siemenellä keskeytyksistä suurin osa oli
  *Paljastui* (11, 5, 4, …), koska ryhmä on tulen alla jo käskyn hetkellä. Siksi ehdotukseen lisättiin
  "tulen alla" -sääntö.
- **Päätös:** `iskuosasto` ei suosi soluttautumista, koska suosittuna tulos oli hieman huonompi. Soluttautuminen on
  pelaajan valittavissa esikatselussa ja valikossa. Se sopii hyökkäyksen aloitukseen ennen kontaktia, jota mittari ei
  mittaa.

### Testit (6c-1)

- `TacticPlannerTests`:
  - soluttautumisen kokoontumispaikka 18–30 m päässä, aseman näkymättömissä
  - reitti kiertää näkyvän ja kuuluvan alueen
  - ei tuliryhmää
  - ehdotus valitsee soluttautumisen, kun tuliasema on sokea ja miehet kokeneita, ja pitkän tulen, kun miehet ovat
    kokemattomia
- `PlannedAttackTests`:
  - hiipiminen ja tulenavauskielto
  - perillä ensin kranaatti, sitten rynnäkkö
  - paljastuminen lähellä johtaa rynnäkköön ja kaukana keskeytykseen
  - tulitoiminta palautuu
- `TacticLoaderTests`: `infiltrate` luetaan ja tarkistetaan.
- `MissionLoaderTests`: `tactics` luetaan.
- Sisältötesti: tehtävien taktiikkatunnukset ovat olemassa.
- `TacticChoiceTests`: kielto ja suosinta.
- `AttackPreviewTests`: soluttautumisen kahvat.
- `PlanTextTests`: perustelu.

## 6c-2 (runko, tarkennetaan omassa suunnitelmassaan)

- `enemy_ai: { counterattack: true, tactic: fire_and_movement }`. Oletuksena on ensimmäinen taktiikka.
- `EnemyCommander` suunnittelee vastahyökkäyksen `TacticPlanner.Propose`lla:
  - asemaan jäävät muodostavat tuliryhmän (uusi parametri: annettu tuliryhmä), ja reservi on iskuryhmä
  - keskeytys päättää hyökkäyksen: tulee jäähtymisaika, ja miehet palaavat asemiinsa.
- Vanha hyökkäys poistetaan: pelaajan tuplaklikkaus ilman taktiikoita ampuu, ja testit siirretään suunnittelijalle.
- Mittari: `probe-attack` molempiin suuntiin (suomalaisten hyökkäys, vihollisen vastahyökkäys), 24 siementä.

### Mittaukset 6c-2 (10.10.2026)

`probe-attack --mission iskuosasto --tactic suggest --seeds 24 --minutes 8`. Mittari laskee nyt myös vihollisen
vastahyökkäykset ja suomalaiset, jotka kaatuvat niiden aikana.

| Ajo | Suomalaisia poissa | Vihollisia poissa | Johtaja kaatui | Vastahyökkäyksiä | Suomalaisia kaatui niissä |
|---|---|---|---|---|---|
| ennen (vanha vastahyökkäys) | 3,5 | 5,9 | 15/24 | 2,2 | 1,7 |
| vihollinen suunnittelee | 3,1 | 4,1 | 8/24 | 2,5 | 1,3 |
| + suojatulen kokemusbonus suunnitellulle tuliryhmälle (kokeiltu, ei otettu) | 3,3 | 3,1 | 4/24 | 2,0 | 1,6 |

- Vanha vastahyökkäys rynnäköi suoraan suomalaisten tuleen, ja suuri osa vihollisen tappioista tuli siitä.
  Suunniteltu vastahyökkäys keskeytyy useimmiten syihin *tuliasema ei näe* tai *asema ei lamaudu*, joten
  vihollinen säästää miehiään. Vastahyökkäyksissä kaatuu myös vähemmän suomalaisia.
- Peli vaikeutui pelaajalle: suomalaiset kaatavat vähemmän vihollisia ja neuvostojohtajan harvemmin. Tämä kuuluu
  tasapainovaiheeseen. Mahdollisia keinoja:
  - tuliaseman näkyvyys poteroihin
  - suojatulen kokemusbonus (kokeiltu yllä, ja se heikensi tulosta)
  - tehtävän vihollismäärä.
- Vastahyökkäys vaatii vähintään neljä taistelukykyistä miestä, koska koukkaus vaatii sen. Jos piilossa olevaa
  kokoontumispaikkaa ei ole, vihollinen ei vastahyökkää, ja kohde lepää minuutin.

