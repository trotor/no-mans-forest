# No Man's Forest – rikkaampi ja vaihtelevampi maasto (vaihe 4n)

Päivämäärä: 2026-09-26
Tila: käyttäjän pyyntö ("kartta on vähän ankean näköinen ja sitä voisi vähän rikastaa ja satunnaistaa"), toteutetaan suoraan
Liittyy: pikseligrafiikka `2026-09-24-pixel-art-design.md`, kartta `2026-09-25-real-map-design.md`

## 1. Tavoite

Taistelukenttä näyttää elävältä Karjalan metsältä eikä toistuvalta tekstuurilta:
- puulajeja ja kokoja on useita
- metsänpohjalla, niityllä ja suolla on yksityiskohtia
- kaatuneet puut ovat oikeita esteitä
- maaston väri vaihtelee laikuittain

**Satunnaisuus** tarkoittaa tässä kartan siemenestä johdettua vaihtelua: sama kartta näyttää aina samalta, mutta mikään kohta ei toistu. Pelisimulaatio pysyy deterministisenä.

## 2. Puut

- **Uusi laji mänty:** harvempi, pyöreämpi ja vaaleampi latvus sekä punaruskea kaarna oksien välissä.
- **Lajin valinta:**
  - Aukon, suon tai niityn reunalla (2 m säteellä) puista on 55 % koivuja ja 15 % mäntyjä.
  - Muualla männyn osuus kasvaa korkeuden mukaan 8 %:sta 58 %:iin, ja koivuja on 15 %.
  - Loput ovat kuusia.
- **Vaihtelu puusta toiseen:**
  - koko 80–125 %
  - vaakasuora peilaus
  - pieni sävyvaihtelu (vaaleus ±6 %, kellertävä ↔ sinertävä)

## 3. Maanpinnan yksityiskohdat

Yksityiskohdat ovat vain grafiikkaa, eivät simulaatiota, ja ne sijoitetaan laikkuina kohinan mukaan:
- **metsänpohja:** saniaiset, sammallaikut, kannot, oksat
- **niitty:** heinätupsut, kukat (valkoiset, keltaiset, violetit)
- **suo:** saraättäät, tupasvilla, tummat allikot
- **tie:** lätäköt pyöränurissa

Ne näkyvät vain lähizoomilla, kuten muutkin koristeet. Ne eivät kasva kiven tai rungon päällä, eikä niillä ole varjoja; kannoilla ja rungoilla on.

## 4. Kaatuneet puut

- **Generaattori** lisää metsään tuulenkaatoja: 3–6 m pituisia runkoja ryhmissä.
- **Este:** korkeus 50 cm, suoja 0,7, näkyvyyden peitto 0,3 ja liikkeen hidastus 1,6. Maassa makaava mies saa rungon takaa hyvän suojan.
- **Piirto:** karttaan kirjoitetaan viivat tyyppiä `log` (alku- ja loppusolu), ja runko piirretään viivan suuntaan ja pituuteen venytettynä.
- **Matala suoja** (`CellData.LowCover`, `LowCoverHeightCm`):
  - Kun esteen korkeus on pienempi kuin maaston oman kasvuston (metsä 15 m), este ei nosta solun suojaa koko korkeudelta. Se on matalaa suojaa omaan korkeuteensa asti.
  - Luoti pysähtyy matalaan suojaan vain alle sen korkeuden.
  - Suojan haku ja kranaattisuoja käyttävät sitä.
  - Sama koskee metsän kiviä (120 cm). Aiemmin kivi pysäytti metsässä luodit 15 metriin asti.

## 5. Maaston sävy

- Shader lisää laikuittain vaihtelua: niittyyn kuivaa ja kukkivaa heinää, metsään sammal- ja neulaskarikelaikkuja, suolle märempiä kohtia.
- Kohina on kartan koordinaateissa, joten laikut pysyvät paikoillaan.

## 6. Testaus

- **Python:**
  - uudet sprite-nauhat ja paletti
  - tuulenkaadot: esteet metsässä, eivät lähtöpisteiden päällä, pisteet TMX:ssä
- **Client:**
  - puulaji maaston mukaan
  - yksityiskohtien tiheys ja deterministisyys
- **Content:** kartta latautuu, ja rungot ovat esteitä.
- **Godot:** käännös ja kuvakaappaukset ennen ja jälkeen.
