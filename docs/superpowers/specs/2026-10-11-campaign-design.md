# Kehitys ja kampanja (eepos 4)

Roadmapin eepos 4 (spec 2026-10-09-roadmap-epics):
- miesten kehitys
- oma ryhmä
- tarina, joka koostuu useista tehtävistä

Käyttäjä 11.10.2026: "jatka eepokseen 4".

## Vaiheet

| Vaihe | Sisältö |
|---|---|
| 9a | **Kampanja ja pysyvä joukko:** kampanjatiedosto, joukko joka säilyy tehtävästä toiseen (kaatuneet pysyvät kaatuneina, haavoittuneet toipuvat), kokemuspisteet, kampanjanäkymä |
| 9b | **Kehitys:** kokemus kasvattaa taitoja; erikoistumiset (tarkka-ampuja, partiomies, pioneeri, lääkintämies) ja niiden vaikutus simulaatiossa |
| 9c | **Oma ryhmä:** miesten ja aseiden valinta tehtävään, värväys kaatuneiden tilalle |
| 9d | **Tarina:** lisää lukuja ja tehtäviä, välitarinat |

## 9a: kampanja ja pysyvä joukko

### Kampanjatiedosto

`content/core/campaigns/<id>/campaign.yaml`:

```yaml
id: jatkosota
title: { fi: "Lehdon joukkue", en: "Lehto's platoon" }
intro: { fi: "...", en: "..." }
roster:                       # koko joukko; samat kentät kuin tehtävän sotilaalla (ei paikkaa)
  - { name: "Alik. Korpela", weapon: suomi_kp31, grenade: m32, leader: true, nerve: 95, ... }
chapters:
  - { mission: iskuosasto, before: { fi: "...", en: "..." }, after: { fi: "...", en: "..." } }
  - { mission: hylky, before: ..., after: ... }
```

### Tehtävän paikat

Tehtävän pelaajan sotilaat ovat **paikkoja**. Eksyneet (esimerkiksi lentäjä) eivät ole paikkoja.

- **Paikka pitää:**
  - sijainnin, ryhmän ja johtajuuden
  - varusteet: sukset, ahkio, esineet
  - aseen ja kranaatin, koska paikka on rooli, esimerkiksi pikakivääriampuja
  - **radiomiehen roolin:** kranaatinheittimen radiomies tunnistetaan paikan järjestysnumerosta, joka otetaan
    talteen ennen korvausta, eikä nimestä
- **Kampanjan mies tuo:** nimen, sisun, moraalin, ampumataidon, kokemuksen ja johtamiskyvyn. Kampanjan tallenteessa
  ei ole asetta. Paikan ase pätee, ja aseen valinta tulee vaiheessa 9c.
- **Täyttö (9a):**
  1. Taistelukykyinen mies, jonka nimi on paikan nimi, saa oman paikkansa. Käskynjaot ja tapahtumatekstit nimeävät
     miehiä, joten kampanjan joukossa on tehtävien nimetyt miehet.
  2. Johtajapaikat (niitä voi olla useita) täytetään parhaasta johtajasta alkaen.
  3. Muut paikat täytetään kokeneimmista alkaen. Tasapelissä ratkaisee joukkoluettelon järjestys.
  4. Jos miehiä on paikkoja vähemmän, viimeiset paikat jäävät tyhjiksi. Jos taistelukykyisiä ei ole lainkaan,
     tehtävä ei käynnisty, ja siitä tulee ilmoitus.
- **Toteutus:** puhdas funktio `CampaignMission.Apply(MissionSpec, CampaignState) → MissionSpec`, jota kutsutaan
  vain kampanjan käynnistyksessä. Tavallinen pelaaminen ja mittarit eivät muutu.
- **Tekstit:** ahkion ja radion ohjeet ovat yleisiä ("ahkiomies"), koska nimetty mies voi kaatua.

### Tehtävän jälkeen

- **Kaatunut** (`Dead`) on poissa lopullisesti.
- **Taistelukyvytön** (`Incapacitated`) toipuu: hän ei ole mukana seuraavassa tehtävässä.
- **Lievästi haavoittunut** on mukana seuraavassa tehtävässä.
- **Kokemuspisteet:**
  - tehtävästä selviäminen: +10
  - jokainen osuma viholliseen: +3
  - jokainen vihollinen, jonka hän kaatoi: +10. Kaatajaksi luetaan viimeinen, joka osui ennen kaatumista, tai käsikähmän
    voittaja. Käsikranaatin heittäjä tunnistetaan kranaatin numerosta heittohetkellä. Kranaatinheittimen osumista ei
    saa pisteitä.
  - Pisteet kerätään tehtävän aikana jokaisen askeleen tapahtumista, koska tapahtumalista lyhenee pitkässä tehtävässä.
  - onnistunut tehtävä: +10 jokaiselle mukana olleelle
- **Onnistuminen** vie kampanjan seuraavaan lukuun. **Epäonnistumisen** jälkeen sama luku pelataan uudelleen, ja tappiot
  pysyvät.

- **Keskeytys:** jos tehtävä aloitetaan alusta tai siitä poistutaan ennen loppua, tappioita ei kirjata. Päätös
  vaiheelle 9a: tämä on pelaajan valinta, ja hän voi yrittää uudelleen.

### Tallennus

`user://campaign.json` sisältää:
- kampanjan tunnisteen
- luvun
- miehet: nimi, ominaisuudet, kokemuspisteet, tila (kunnossa / toipuu / kaatunut) ja tehtävät
- historian

Rikkinäinen tiedosto tarkoittaa uutta kampanjaa.

### Pelaajalle

- **Päävalikko:** *Kampanja*-ryhmän ensimmäinen nappi on *Jatka kampanjaa* tai *Aloita kampanja*.
- **Kampanjanäkymä:**
  - luvun tarina ja tehtävän nimi
  - joukko: nimi, ase, kokemuspisteet ja tila
  - napit *Aloita tehtävä*, *Aloita kampanja alusta* ja *Takaisin*
- **Tehtävän loppuruutu:**
  - kampanjassa *Takaisin kampanjaan*
  - jokaisen miehen saamat kokemuspisteet ja hänen tilansa

### Testit

- `CampaignLoaderTests`: kampanja luetaan ja tarkistetaan (tehtävät ovat olemassa, joukossa on johtaja).
- `CampaignTests`:
  - paikkojen täyttö: nimetyt, useita johtajia, liian vähän miehiä, ei yhtään taistelukykyistä
  - radiomies, kun Virtasen paikalla on toinen mies
  - kaatuneet ja toipuvat
  - kokemuspisteet tapahtumista
  - luvun eteneminen
  - epäonnistuminen
  - JSON edestakaisin ja rikkinäinen tiedosto
- Client: kampanjan tehtävän luonti (paikat täyttyvät kampanjan miehillä).
