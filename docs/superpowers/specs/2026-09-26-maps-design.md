# No Man's Forest – kunnollinen paperikartta ja tehtäväkartta (vaihe 4d)

Päivämäärä: 2026-09-26
Tila: käyttäjän pyyntö ("tuo kartta on karmean näköinen, pitäisi panostaa sen luomiseen enemmän; ohjeiden lisäksi tarvitaan tehtäväkartta"), toteutetaan suoraan

## 1. Paperikartta (M)

**Tyyli:** suomalaisen peruskartan henki.

| Kohde | Esitys |
|---|---|
| Paperi | kermanvaalea, hieman elävä |
| Metsä | vaalean harmaanvihreä |
| Avoin maa (aukea, pelto) | vaaleankeltainen |
| Suo | vaalean sinertävä pohja ja lyhyet vaakasuorat siniset katkoviivat |
| Vesi | sininen pinta ja tummempi rantaviiva |
| Tie | ruskea täyttö ja tummat reunaviivat |
| Kivet | tummat pisteet |
| Korkeuskäyrät | ruskeat, 5 m välein; joka viides (25 m) paksumpi |
| Rinnevarjostus | heikko, valo luoteesta |
| Ruudukko | 100 m välein, reunoilla kirjaimet (A–J) ja numerot (1–10) |

**Laatu:**
- **Korkeudet:** kartan korkeudet pehmennetään (noin 3 m säde) ennen käyriä, joten 25 cm portaat ja kumparekohina eivät tee käyriä tasamaalle.
- **Käyrät** piirretään tasavahvoina ja reunat tasoittaen (etäisyys käyrään = jäännös / kaltevuus).
- **Maastorajat ja tiet** ovat pehmeitä: maskit sumennetaan ennen värien sekoitusta.
- **Tarkkuus:** koko kartta 1 px/m, tehtäväkartta 2 px/m.

## 2. Tehtäväkartta

- **Sijainti:** käskypaperin oikealla puolella. Leveällä ruudulla tekstin vieressä, kapealla sen alla.
- **Rajaus:** alueiden ja suunnitelman pisteiden ympäri, 60 m reunus, neliö, kartan rajoissa.
- **Pohja:** sama paperikartta 2 px/m.
- **Päälle piirretään käsin piirretyn näköisesti:**
  - lähtöalue sinisenä kehyksenä
  - ilmoitettu vihollisasema punaisena, vinoviivoin täytettynä
  - suunnitelman nuolet: hyökkäys (`attack`) sininen yhtenäinen, paksu ja nuolenkärjellä; paluu (`withdraw`) sininen katkoviiva
  - tavoitteiden numerot
  - pohjoisnuoli ja 50 m mittakaava
  - otsikko "TEHTÄVÄKARTTA" / "MISSION MAP"

## 3. mission.yaml: `plan`

```yaml
plan:
  - { kind: attack,   points: [[250, 818], [330, 700], [410, 612], [466, 556]] }   # metres, x east y south
  - { kind: withdraw, points: [[466, 556], [380, 680], [252, 816]] }
```

**Tarkistukset:**
- `kind` on `attack` tai `withdraw`
- nuolessa on vähintään 2 pistettä
- pisteet ovat kartan rajoissa (tarkistetaan kartan kanssa)

## 4. Testaus

- **PaperMap:**
  - koko ja rajaus
  - rinteessä käyrät säännöllisin välein, tasamaalla kumparekohinasta huolimatta ei yhtään
  - reunat ovat tasoitettuja (välisävyjä)
  - vesi, tie ja suon viivat oikean värisiä
- **MissionMap:** rajaus sisältää alueet ja reitit, ja se pysyy kartan sisällä.
- **Lataaja:** `plan`, virheet sekä Iskuosaston reitit kartalla ja kuljettavissa soluissa.
- **Godot:** kuvakaappaukset käskystä ja kartasta.
