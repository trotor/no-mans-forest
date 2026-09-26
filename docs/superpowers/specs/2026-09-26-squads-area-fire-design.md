# No Man's Forest – kaksi ryhmää ja aluetuli (vaihe 4j)

Päivämäärä: 2026-09-26
Tila: käyttäjän pyyntö ("aloita kahdesta ryhmästä ja aluetulesta"), toteutetaan suoraan
Liittyy: hyökkäys `2026-09-26-attack-design.md`, vihollisen aloitteellisuus `2026-09-26-enemy-initiative-design.md`, tehtävät `2026-09-26-missions-design.md`

## 1. Tavoite

- **Kaksi ryhmää kummallekin puolelle:** jokaisella ryhmällä on oma johtajansa. Pelaaja voi komentaa koko joukkuetta, yhtä ryhmää tai yksittäisiä miehiä. Näin ryhmätason tuli ja liike onnistuu: toinen ryhmä sitoo, toinen koukkaa.
- **Aluetuli:** pelaaja voi käskeä ampumaan paikkaan, jossa vihollinen viimeksi nähtiin tai kuultiin, tai mihin tahansa maaston kohtaan. Hyökkäyksen suojaava puolisko ja vihollisen tukiryhmä käyttävät aluetulta itse, kun kohde ei näy.

**Rajattu pois:** useampi kuin kaksi ryhmää käyttöliittymässä (malli tukee useampaa), ryhmän muodostelmat, joukkueenjohtaja ja radiot.

## 2. Ryhmät

### 2.1 Tehtävätiedosto

```yaml
squads:                      # nimet; järjestys = ryhmien järjestys
  strike:  { en: "Strike squad", fi: "Iskuryhmä" }
  support: { en: "Support squad", fi: "Tukiryhmä" }
forces:
  player:
    - { name: "Alik. Korpela", squad: strike, leader: true, ... }
```

- Miehellä ilman `squad`-kenttää on puolensa ensimmäinen ryhmä.
- Tuntematon ryhmä on virhe.
- Jokaisella ryhmällä voi olla oma johtajansa (`leader: true`).

### 2.2 Simulaatio

- **Ryhmä:** `Unit.Squad` on ryhmän järjestysnumero puolen sisällä (0, 1, …). Se on tilatiivisteessä.
- **Johtaja** on ryhmäkohtainen:
  - johtajan läheisyyden moraali- ja lamautusbonus koskee vain oman ryhmän miehiä
  - kokoaminen ja seuraanto tapahtuvat vain oman ryhmän sisällä
  - johtajan kaatumisen moraalitappio koskee vain hänen ryhmäänsä
- **Hyökkäys** (vaihe 4e) toimii ennallaan: se muodostetaan käskyn saaneista miehistä ryhmästä riippumatta.

### 2.3 Vihollisen komentaja

**Kaksi ryhmää:**
- Kohdetta lähempänä oleva ryhmä on **asemassa** ja antaa tulta.
- Toinen ryhmä on **reservi** ja tekee vastahyökkäyksen, jos siinä on vähintään kaksi kunnossa olevaa miestä.
- Huudon päästää hyökkäävän ryhmän johtaja, tai jos hän ei ole kunnossa, kuka tahansa kunnossa oleva johtaja.
- Jos reservissä ei ole tarpeeksi kunnossa olevia, hyökkää asemassa oleva ryhmä kuten ennen, pikakivääri paitsi.

**Yksi ryhmä:** kuten ennen.

**Asemassa oleva ryhmä** ampuu vastahyökkäyksen aikana aluetulta kohteen uskottuun paikkaan, jos kohde ei näy.

### 2.4 Iskuosasto

**Suomalaiset** (7):

| Ryhmä | Mies | Ase | Sisu | Kokemus |
|---|---|---|---|---|
| Iskuryhmä | Alik. Korpela, johtaja | KP/31 | 95 | 90 |
| Iskuryhmä | Sotm. Mäkinen, tarkka-ampuja | M/39 | 80 | 80 |
| Iskuryhmä | Sotm. Laine | M/39 | 90 | 75 |
| Iskuryhmä | Sotm. Hakala | KP/31 | 85 | 75 |
| Tukiryhmä | Kpl. Virtanen, johtaja | Lahti-Saloranta | 85 | 85 |
| Tukiryhmä | Sotm. Nieminen | M/39 | 80 | 70 |
| Tukiryhmä | Sotm. Rantanen | M/39 | 80 | 70 |

**Neuvostoliittolaiset** (9):
- kumpareen asemaryhmä: Belov käskyineen, Kuznetsov DP-27:n kanssa, Orlov, Sidorenko ja Gusev
- reserviryhmä noin 60 m kumpareen takana: Ml. serž. Petrov (PPŠ, johtaja), Volkov, Smirnov ja Ivanov (kiväärit), kokemus 35–55

**Kartta:** generaattori lisää tukiryhmälle 3 lähtöpistettä iskuryhmän itäpuolelle ja reserville 4 pistettä kumpareen taakse.

### 2.5 Käyttöliittymä

- **Kortit** ryhmitellään ryhmittäin, ja ryhmän otsikko on painike, joka valitsee koko ryhmän.
- **Valinta:**
  - Tuplaklikkaus omaan mieheen valitsee hänen ryhmänsä (ennen: tyhjensi valinnan).
  - Oikea klikkaus tai Esc tyhjentää valinnan, jolloin komennetaan koko joukkuetta.
  - Jos kentällä on vain yksi ryhmä, ryhmän valinta tarkoittaa koko joukkuetta, kuten ennenkin.
- **Kortit kapenivat,** jotta seitsemän mahtuu ruudulle. Alarivillä lukee esimerkiksi "71+2 · 2 gr · Free fire", ja täysi selitys näkyy vihjeenä.
- **Tilarivi** näyttää "Commanding: platoon", ryhmän nimen tai miesten nimet.
- **Tehtäväkäskyn kokoonpano** näytetään ryhmittäin otsikoineen.

## 3. Aluetuli

- **Käsky:** `AreaFireOrder(sotilas, paikka)`.
  - Hylätään, jos miehellä ei ole asetta tai varalippaita ("no spare magazines"), koska viimeinen lipas jää itsepuolustukseen, tai jos paikka on kantaman ulkopuolella ("out of range").
  - Käsky pyyhkii liikkeen, tutkimisen, rynnäkön ja maalin, ja mies irtoaa hyökkäyksestä.
- **Ampuminen:**
  - Mies tähtää ja ampuu sarjoja paikkaan (maa + 60 cm) tavallisella hajonnalla, eli asento, lamautus, taito ja kokemus vaikuttavat. Maalin liike ei vaikuta.
  - Aluetuli ohittaa tulenavauskiellon, koska se on nimenomainen käsky.
  - Ystävää ei ammuta: jos oma mies on tulilinjalla, mies odottaa.
- **Lamautus:** jokainen vihollinen, joka on alle 5 m päässä osumakohdasta tai tähtäyspisteestä, saa lamautuksen kuin häntä ammuttaisiin (tähdätyn ohilaukauksen säde). Tulilinjan lähellä olevat saavat lamautuksen tavalliseen tapaan (2,5 m).
- **Itsepuolustus:** jos näkyvä vihollinen on alle 30 m päässä, mies ampuu häntä ja palaa sitten aluetuleen.
- **Päättyminen:**
  - toinen käsky
  - viimeinen lipas: kun varalippaita ei enää ole, viimeinen jää itsepuolustukseen
  - käskyn antaminen tulee pelaajalta, hyökkäyssuunnitelmalta tai vihollisen komentajalta
- **Hyökkäys:** suojaava puolisko ampuu aluetulta kohteen uskottuun paikkaan, kun kohde ei näy, ja lopettaa, kun kohde näkyy taas.
- **Käyttöliittymä:**
  - Ctrl/Cmd + klikkaus maastoon tai klikkaus "?"-merkkiin (viimeksi nähty tai kuultu) käskee komennossa olevat ampumaan aluetulta.
  - Tuloksena on `ClickResult.AreaFireOrdered`, tulimerkki ja käskyn välähdys.
  - Aluetulta ampuvilta komennossa olevilta miehiltä piirretään katkoviiva paikkaan.
  - Kortin tila on "Area fire".

## 4. Tasapainokoe

Iskuosasto: 7 suomalaista vastaan 9 neuvostomiestä, kontakti 100 m, koko joukkue saa käskyn, 8 siementä, 3 min.

| Tapa | Suomalaisia pois taistelusta | Vihollisia pois taistelusta | Kohde kaatui |
|---|---|---|---|
| Tuli- ja liikehyökkäys | 3,3 / 7 | 3,5 / 9 | 8 / 8 |
| Suora rynnäkkö | 5,6 / 7 | 0,3 / 9 | 1 / 8 |

## 5. Testaus

- **Sim:**
  - johtajan vaikutus vain omaan ryhmään
  - seuraanto ryhmän sisällä
  - aluetuli: ampuu, lamauttaa lähellä olevat, pysähtyy viimeiseen lippaaseen, itsepuolustus, hylkäykset, käskyt pyyhkivät
  - hyökkäyksen suojatuli piilossa olevaan kohteeseen
  - komentaja: reservi hyökkää, asema ampuu
  - deterministisyys
- **Content:** ryhmät ja virheet; Iskuosasto 7 + 9 ja kaksi ryhmää kummallakin.
- **Python:** lähtöpisteet ovat kuljettavia, ja niitä on tarpeeksi.
- **Client:**
  - ryhmän valinta tuplaklikkauksella
  - komentoteksti
  - aluetulen klikkaukset
  - kortin tila
- **Godot:** käännös ja kuvakaappaus.
