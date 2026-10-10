# Kranaatinheitintuki (vaihe 8c, skenaarioiden eepos)

Eepos 3 (spec 2026-10-09-roadmap-epics) vaihe 3 ja pääspecin §8.7: radiomies, tulenjohto, viive, tähystyslaukaus
ja korjaukset sekä rajalliset ammukset. Käyttäjä 10.10.2026: "tee päätöksiä ja ala toteuttaa".

## 1. Tehtävä

```yaml
support:
  mortar: { he: 8, delay_s: [60, 120], radio: "Kpl. Virtanen" }
```

- **Radiomies:** nimetty pelaajan mies. Tulta voi tilata vain, kun hän on taistelukykyinen eikä lamautunut tai
  pakeneva. Jos hän kaatuu, tuki on menetetty.
- **Ammukset:** rajalliset (`he`). Jokainen laukaus, myös tähystyslaukaus, kuluttaa yhden.
- **Savu** tulee myöhemmin, koska se vaatii näkyvyyslaskentaan savupilvet (päätös: ei tässä vaiheessa).

## 2. Tulen kulku (`FireMissionOrder(Radioman, Target, Rounds)`)

1. **Tilaus radiolla** kestää 10 s. Yhteys onnistuu 85 % todennäköisyydellä. Jos se epäonnistuu, tulee ilmoitus
   *Yhteys ei toimi*, ja tilaus raukeaa.
2. **Viive:** 60–120 s ennen ensimmäistä laukausta (satunnainen, ja siemen määrää sen).
3. **Tähystyslaukaus** osuu kohteen lähelle. Ensimmäinen virhe on 40–60 m satunnaiseen suuntaan.
   - Jos joku pelaajan taistelukykyisistä näkee iskupaikan (näkölinja silmästä iskupaikkaan enintään 400 m), korjaus
     puolittaa virheen.
   - Jos kukaan ei näe iskua, virhe pysyy samana uuteen satunnaiseen suuntaan.
   - Tähystyslaukauksia ammutaan 20 s välein, kunnes virhe on alle 15 m tai laukauksia on ammuttu 4. Sen jälkeen
     siirrytään tehokkaaseen tuleen.
4. **Tehokas tuli:** pyydetty määrä laukauksia (oletus 4) 3 s välein. Jokainen osuu kohtaan, joka saadaan
   jäljelle jääneestä virheestä ja ±8 m hajonnasta.
5. **Ammusten loppuminen** lopettaa tulen kesken.

Yhdellä puolella on yksi tulitehtävä kerrallaan. Uusi tilaus korvaa vanhan, jos vanha on vielä tilausvaiheessa tai
odottaa. Kesken olevaa tulta ei voi korvata.

## 3. Isku

- Kranaatti on kuin käsikranaatti, mutta isompi: räjähdyssäde 30 m, tappava säde 12 m, tappavuus 80 % ja lamautus
  900. Vahinko lasketaan samalla funktiolla kuin käsikranaatissa.
- Kranaatti voi osua omiin, joten tuli liian lähelle omia on vaarallista.
- **Ääni:** 2 s ennen iskua kuuluu ulvova lento (`ShellIncoming`). Isku on räjähdys (`ShellExploded`).

## 4. Pelaajalle

- **Oikean napin valikko maastossa:** *Kranaatinheitintuli tänne (HE 8)*, kun tuki on käytettävissä. Käsky menee
  radiomiehelle, vaikka häntä ei olisi valittu.
- **Tila ylärivillä:** *Kranaatinheitin HE 8 · tilaus…*, *iskut 45 s*, *tähystys*, *tuli* tai *ei yhteyttä*.
- **Ilmoitukset:**
  - *Tuli tilattu*
  - *Yhteys ei toimi*
  - *Tähystyslaukaus tulossa*
  - *Tehokas tuli*
  - *Tuli päättyi*
  - *Kranaatit loppuivat*
- **Kartalla** kohde näkyy katkoviivaympyränä, kunnes tuli päättyy.

## 5. Sisältö

`iskuosasto`: Kpl. Virtanen on radiomies, ja tukena on 8 HE-kranaattia. Jotta tämä ei muuta mittarin ajoa, mittari
ei käytä tukea. Pelaajalle tuki on uusi keino.

## 6. Testit

- `MortarTests`:
  - tilaus kestää 10 s, ja yhteyskatkoksen sattuessa tilaus raukeaa
  - viive on 60–120 s
  - tähystys: virhe puolittuu nähtynä ja pysyy näkemättömänä
  - tehokkaan tulen määrä ja väli
  - ammukset kuluvat ja loppuvat
  - radiomiehen kaatuminen estää uudet tilaukset
  - ilman tukea käsky hylätään
  - sama siemen antaa saman tuloksen
  - isku vahingoittaa ja lamauttaa
- `MissionLoaderTests`: `support.mortar` luetaan ja tarkistetaan (radiomies on pelaajan mies).
- Client:
  - valikon rivi
  - tilateksti
  - ilmoitukset
