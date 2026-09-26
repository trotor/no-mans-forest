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

## 4. Tapahtumasignaalit kaukaa katsottuna

Käyttäjän lisäpyyntö: "isommilla zoomitasoilla laukaukset ja muut voisivat näkyä signaaleina sieltä alueelta, niin että ihmisen on helpompi ne havaita".

**Signaalit ja niiden paikka:**

| Signaali | Paikka |
|---|---|
| Tuli (keltainen sykkivä rengas) | ampujan kohdalla, jos pelaaja näkee ampujan; muuten kohdassa, johon luoti iskee |
| Räjähdys (oranssi tähti) | räjähdyskohdassa (aina) |
| Osuma omaan (punainen risti) | haavoittuneen tai kaatuneen oman miehen kohdalla |
| Vihollinen kaatui (punainen X) | nähdyn vihollisen kohdalla |

**Kesto ja yhdistäminen:**
- Signaali kestää 1,2 s.
- Samanlainen signaali alle 15 m päässä tulevan signaalin kohdalla uusitaan eikä lisätä uutta, joten sarjatuli on yksi sykkivä rengas.

**Näkyvyys:**
- **Pelinäkymä:** signaalit näkyvät, kun zoom on alle 0,4, ja niiden koko on näytöllä vakio (noin 28 px). Lähempää katsottuna varsinaiset tehosteet riittävät.
- **Paperikartta:** signaalit näkyvät aina, kun kartta on auki.

## 5. Testaus

- **PaperMap:**
  - koko ja rajaus
  - rinteessä käyrät säännöllisin välein, tasamaalla kumparekohinasta huolimatta ei yhtään
  - reunat ovat tasoitettuja (välisävyjä)
  - vesi, tie ja suon viivat oikean värisiä
- **MissionMap:** rajaus sisältää alueet ja reitit, ja se pysyy kartan sisällä.
- **Lataaja:** `plan`, virheet sekä Iskuosaston reitit kartalla ja kuljettavissa soluissa.
- **Signaalit:** paikka näkyvälle ja piilossa olevalle ampujalle, räjähdys, oma osuma, nähty kaatunut vihollinen, yhdistäminen ja vanheneminen.
- **Godot:** kuvakaappaukset käskystä, kartasta ja signaaleista.
