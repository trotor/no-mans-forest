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
  - **Paljastuminen:** jos joku joutuu tulen alle ennen kuin kaikki ovat perillä, ryhmä rynnäköi heti, kun se on
    enintään 40 m päässä asemasta. Kauempana hyökkäys keskeytyy syystä *Paljastui* (`HaltReason.Discovered`), ja
    pelaaja päättää jatkosta.
  - **Assault:** kun kaikki ovat perillä tai hiipimisen aikaraja (4 min) on täynnä, ne, joilla on kranaatti ja jotka
    voivat heittää tunnettuun mieheen, heittävät ensin. Sitten kaikki rynnäköivät, ja rynnäkkö heittää lisää
    kranaatteja matkalla kuten ennenkin. Lamautusta ei odoteta.
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
  - vihollisia tiedetään vähintään yhtä monta kuin meitä.

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
