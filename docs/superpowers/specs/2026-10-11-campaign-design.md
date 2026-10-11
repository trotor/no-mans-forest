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

## 9b: kehitys ja erikoistumiset

Käyttäjä 11.10.2026: "jatka" (eepos 4, vaihe 9b). Päätökset tein itse ("tee päätöksiä ja ala toteuttaa").

### Kehitys (vain kampanjassa)

Kokemuspisteistä (Xp) saa tason:

| Taso | Xp | fi | en |
|---|---|---|---|
| 1 | 0 | Alokas | Recruit |
| 2 | 25 | Kokenut | Seasoned |
| 3 | 70 | Veteraani | Veteran |
| 4 | 140 | Konkari | Old hand |

Tehtävän kirjauksessa (CampaignRecord) kasvu tapahtuu näin:
- **Joka ylitetty tasoraja:** kokemus (Experience) +5 ja hermo (Nerve) +5. Jos tehtävä ylittää kaksi rajaa, lisäys tulee kahdesti.
- **Osumat:** ampumataito (Marksmanship) kasvaa +1 kahta osumaa kohden, enintään +4 tehtävästä.
- **Johtaminen:** mies, joka on tehtävän lopussa johtaja ja selvisi kaatumatta, saa johtamistaitoa (Leadership) +5. Tähän lasketaan myös kesken tehtävän johdon ottanut.
- **Katto:** kaikki taidot ovat enintään 100, mikä on loaderin raja.

Mitoituksesta: onnistuneesta tehtävästä selvinnyt saa 20 pistettä, ja lisäksi 3 jokaisesta osumasta ja 10 jokaisesta kaadosta. Taso 2 on siis saavutettavissa ensimmäisen luvun jälkeen, ja erikoistumiset näkyvät hylyssä. Veteraani on nykyisellä sisällöllä hädin tuskin saavutettavissa, Konkari ei lainkaan. Rajat katsotaan uudelleen vaiheessa 9d, kun lukuja on enemmän.

Kampanjanäkymä näyttää miehen tason nimen. Pelkkä "kokemus {Xp}" olisi ollut epäselvä, koska nyt myös Experience-ominaisuus kasvaa.

### Erikoistuminen

Erikoistumisen (Specialty: None, Marksman, Scout, Sapper, Medic) voi antaa miehelle, jos:
- hän on vähintään tasolla 2
- hän ei ole kaatunut
- hänellä ei vielä ole erikoistumista

Pelaaja valitsee sen kampanjanäkymässä, kerran ja pysyvästi. Kampanjan miehistössä (roster) erikoistumisen voi antaa myös tiedostossa (`specialty:`), ja samoin tehtävän `mission.yaml`-tiedostossa sotilaalle. Perustehtävissä (iskuosasto, hylky) erikoistumisia ei ole, joten mittarit pysyvät ennallaan.

`Apply`: erikoistuminen kuuluu miehen taitoihin. Kampanjamiehen erikoistuminen korvaa siis paikan oman, myös silloin kun miehellä ei ole erikoistumista. `Fill` ei katso erikoistumisia, joten tarkka-ampuja voi päätyä konepistoolimiehen paikalle. Paikkojen valinta tulee vaiheessa 9c.

Simulaatiossa erikoistumiset vaikuttavat näin:

| Erikoistuminen | Vaikutus |
|---|---|
| **Tarkka-ampuja** (Marksman) | Kiväärin (WeaponClass.Rifle) hajonta ×70 %. |
| **Partiomies** (Scout) | `ObserverPct` +20. Liikkuessa näkyvyys ×70 %, kuitenkin vähintään 100 %. Ampuessa näkyvyys ei muutu. Liikkeen äänen säde ×70 % kaikissa liikkumistavoissa (`NoiseRadiusCm(Unit)`, jota näkö- ja valppausjärjestelmät käyttävät). |
| **Pioneeri** (Sapper) | Kaksi käsikranaattia lisää, jos hänellä on kranaatteja. Kantokatto on `MaxGrenades` + 2. Kranaatin hajonta puolittuu. Puhelinjohdon katkaisu kestää 5 s (muilla 10 s). |
| **Lääkintämies** (Medic) | Ensiapu, ks. alla. |

### Ensiapu

Lääkintämies sitoo oman puolen vakavasti haavoittuneen (Serious) haavan. Sitominen kestää 15 s, ja sen jälkeen vakava haava ei enää vuoda taistelukyvyttömyyteen (`Unit.Bandaged`). Uusi haava poistaa siteen.

- **Käsky:** pelaaja antaa käskyn `FirstAidOrder(medic, patient)`. Hylätään, jos:
  - antaja ei ole lääkintämies
  - potilas ei ole oma, vakavasti haavoittunut ja sitomaton
  - potilasta kannetaan
  - antaja on pinned (tavallinen sääntö)

  Lääkintämies voi sitoa myös itsensä.
- **Kulku:** lääkintämies menee potilaan luo ja seuraa tätä, jos potilas liikkuu. Sitominen etenee vain, kun potilas on 1,5 m:n päässä ja kumpikin on paikallaan. Jos potilas loittonee, sitominen alkaa alusta.
- **Keskeytys:**
  - potilas ei enää tarvitse sidettä (on sidottu, ei ole Serious, on taistelukyvytön tai kuollut)
  - potilasta kannetaan
  - lääkintämies murtuu, joutuu pinned-tilaan tai menee taistelukyvyttömäksi
  - lääkintämies saa minkä tahansa muun käskyn

  Keskeytys tehdään yhdessä paikassa, käskyjen käsittelyn alussa samalla tavalla kuin `PhoneSystem.Abandon`, sekä `SetWound`:n out-of-action-haarassa.
- **Itsestään:** lääkintämies ryhtyy sitomaan omin päin, kun hänellä ei ole muuta tekemistä:
  - liike, toiminta, ryöstö-, kanto-, rynnäkkö- ja hyökkäystehtävä puuttuvat
  - hän on Steady
  - häneen ei ole kohdistunut tulta 5 sekuntiin

  Hän valitsee lähimmän sitomattoman Serious-miehen 20 m:n sisältä, mukaan lukien itsensä. Tasapelissä ratkaisee pienempi `UnitId`. Valinta on deterministinen eikä käytä satunnaislukuja.
- **Jonot:** ensiapukäsky ei mene Shift-jonoihin.

Pelaajalle:
- valikon rivi *Sido haava* näkyy, kun valittuna on lääkintämies ja kohteena oma sitomaton Serious-mies
- kortin tila on *Sitoo haavaa*
- kortin ja vihjeen rivillä lukee erikoistuminen
- potilaan tilassa lukee *sidottu*

### Testit

- Taso Xp:stä. Kahden rajan ylitys kerralla antaa kaksinkertaisen kasvun, ja taidoilla on katto.
- Ampumataidon ja johtamistaidon kasvu.
- Erikoistumisen ehdot. `Apply` vie erikoistumisen. JSON-tallennus säilyttää sen ja säilyy taaksepäin yhteensopivana.
- Simulaatio:
  - tarkka-ampujan hajonta vain kiväärillä
  - partiomiehen havainto ja näkyvyys
  - äänen säde
  - pioneerin kranaatit, kantokatto, hajonta ja katkaisuaika
  - ensiavun käsky, kulku, keskeytykset ja sidotun miehen vuotamattomuus
  - uusi haava poistaa siteen
  - automaattinen valinta
  - determinismi (StateHash)
- Mittarit: iskuosasto pysyy tavulleen samana (PROBE-IDENTICAL), ja hylyn pelastuksia on yhä 13/24.
