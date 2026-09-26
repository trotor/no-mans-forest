# Maasto satelliittikuvasta (vaihe 4p)

## Tavoite

Karhumäen kartan aukot ja suot näyttivät keinotekoisilta. Ne olivat kohinalla tehtyjä pyöreitä läiskiä ja OSM-monikulmioiden
suoria reunoja. Nyt metsän, aukkojen ja soiden muodot tulevat alueen oikeasta satelliittikuvasta. Reunat ovat
rosoiset kuten luonnossa.

## Lähteet

- **Sentinel-2 L2A** (Copernicus, 10 m) Earth Search STAC -rajapinnasta.
  - Valitaan kesä–elokuun kohtaukset pilvisyyden mukaan, selkein ensin.
  - Käytetään ensimmäistä kohtausta, jossa tyhjiä pikseleitä on alle 0,1 %. Näin pois jää kohtaus, jonka kaistan reuna
    leikkaa alueen.
  - Kaistat: punainen, vihreä, sininen ja lähi-infrapuna. Niistä lasketaan NDVI.
- **ESA WorldCover 10 m 2021 v200** (CC BY 4.0), maanpeitteen luokat.
- Molemmat projisoidaan `rasterio.warp.reproject`illa pelin 10 m ruudukolle. Ruudukko käyttää samaa lineaarista
  paikallista projektiota kuin generaattori.
- Tallennetaan tiedostoihin `tools/mapgen/data/<alue>/`:
  - `satellite.png` (RGB)
  - `ndvi.png`
  - `worldcover.png`
  - `land.json` (kohtaus, päivä, lähdemerkintä)
- `rasterio` tarvitaan vain hakuun. Generaattori toimii ilman verkkoa ja ilman rasteriota.

## Generointi

1. **Avoimuus.** Avoimuus on kirkkauden z-arvo plus (1 − NDVI):n z-arvo.
   - Tiet ovat kirkkaita, joten tien 10 m ruudut ja niiden naapurit saavat avoimuuden 30. persentiilin.
   - Näin tien varteen ei synny aukkokaistaa.
2. **Luonnollinen muoto.** Avoimuus skaalataan bilineaarisesti 1 m ruudukolle, ja siihen lisätään kahden mittakaavan
   kohinaa (`ragged`).
   - Kynnys valitaan niin, että noin 11 % maasta aukeaa (`OPEN_SHARE`).
   - Tulos siivotaan avauksella ja sulkemisella (`clean`). Silloin irtopisteitä tai neulanreikiä ei jää.
3. **WorldCover.** Luokat 20–60 (pensaikko, niitty, pelto, rakennettu, paljas) lisätään aukkoihin. Luokat 90 ja 100
   (kosteikko, sammal) muuttuvat suoksi.
4. **OSM-suot ja -niityt** rosoitetaan samalla tavalla (`roughen`): pehmeä maski, kohina ja siivous. Suon suora
   monikulmioreuna muuttuu luontevaksi.
5. Vanha elliptinen pelto ja kohinalla tehdyt aukot jäävät käyttöön vain, jos satelliittidataa ei ole.
6. Kartan `source`-ominaisuus kertoo myös satelliittilähteet.

## Testit

`tools/mapgen/tests`, synteettinen 20 × 20 maa:

- Aukot seuraavat kuvan avointa vyöhykettä: vähintään 80 % aukoista on 35 ruudun sisällä siitä.
- Aukon reuna on rosoinen: vasemman reunan keskihajonta on yli 1,5 ruutua.
- WorldCoverin kosteikko muuttuu suoksi.

Oikea kartta:

- Niittyä on 5–25 % maasta.
- Lähdemerkintä mainitsee Copernicuksen.

C#-sisältötestit tarkistavat, että kartta latautuu ja lähtöpaikat, poterot ja kaatuneet puut ovat kunnossa.

## Tulos

Karhumäki, kohtaus S2A_36VWQ_20250718:

| Maasto | Osuus |
|---|---|
| Metsä | 79 % |
| Aukko | 11 % |
| Suo | 8 % |
| Vesi | 1 % |

Aukot ovat epäsäännöllisiä hakkuita ja niittyjä satelliittikuvan kirkkaiden kohtien paikalla.
