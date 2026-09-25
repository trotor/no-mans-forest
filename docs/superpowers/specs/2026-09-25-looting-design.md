# No Man's Forest – ryöstö kaatuneilta (vaihe 3c)

Päivämäärä: 2026-09-25
Tila: käyttäjän pyyntö ("lisää loot-mahdollisuus kuolleilta: tehtävän papereita, aseita, patruunoita"), toteutetaan suoraan
Liittyy: taistelumalli `2026-09-24-combat-design.md`, kranaatit ja ohjaus `2026-09-24-grenades-melee-design.md`

## 1. Tavoite

Kaatuneilta (kuolleet, toimintakyvyttömät ja vangit) voi ottaa:
- patruunoita (vararipaita)
- kranaatteja
- aseen
- tehtävän papereita

Ohjaus pysyy yksinkertaisena:
- **Klikkaus kaatuneeseen:** lähin komennossa oleva mies käy tutkimassa hänet.
- **Itsenäinen ryöstö:** ammukset lopussa oleva sotilas hakee itse patruunoita lähellä olevilta kaatuneilta, kun tilanne on rauhallinen.

**Rajattu pois:**
- esineiden pudottaminen ja vaihtaminen miesten kesken
- inventaarioruutu
- tehtävätavoitteet papereille (tulevat tehtäväjärjestelmän mukana)
- aseiden maahan jättäminen ilman kaatunutta

## 2. Rajalliset ammukset

- **Aseen data:** uusi valinnainen kenttä `spare_magazines`, eli vararipaiden tai -lippaiden määrä alussa. Oletus on 4.

  | Ase | `spare_magazines` |
  |---|---|
  | Mosin-Nagant M/39 ja M/91-30 | 12 (5 patruunan lipas) |
  | Suomi KP/31 ja PPSh-41 | 2 (71 patruunan rumpu) |
  | Lahti-Saloranta M/26 | 6 (20 patruunan lipas) |
  | DP-27 | 3 (47 patruunan lautanen) |

- **Lippaanvaihto** kuluttaa yhden varalippaan. Ilman varalippaita lippaan vaihto ei ala.
- **Patruunat loppu:** kun lipas on tyhjä eikä varalippaita ole, sotilas ei ammu. Tila näkyy korttina "Out of ammo".

## 3. Mitä kaatuneelta löytyy

Tutkija ottaa kerralla kaiken, mitä hän voi käyttää. Kaatunut on tutkittu (`Looted`) vasta, kun hänellä ei ole enää mitään kenellekään hyödyllistä: ei kranaatteja eikä asetta, jossa on patruunoita tai varalippaita. Muuten laukkumerkki jää, ja toinen mies voi vielä ottaa loput.

- **Patruunat:** jos kaatuneella on sama ase kuin tutkijalla, tutkija saa kaatuneen varalippaat. Jos kaatuneen aseessa on täysi lipas, tutkija saa senkin varalippaaksi. Vajaa lipas jää.
- **Kranaatit:** tutkija ottaa kaatuneen kranaatit, jos ne ovat samaa tyyppiä kuin hänen omansa tai jos hänellä ei ole kranaatteja. Jälkimmäisessä tapauksessa hänen kranaattityyppinsä vaihtuu.
- **Ase:** tutkija vaihtaa aseen, jos hänellä ei ole asetta tai hänen omansa on tyhjä (lipas ja varalippaat), ja kaatuneen aseessa on patruunoita. Tutkijan tyhjä ase jää kaatuneelle.
- **Paperit:** kaikki esineet (`Item(id, name)`) siirtyvät tutkijalle. Paperit kulkevat tutkijan mukana. Jos hän kaatuu, ne jäävät hänelle ja voidaan ottaa taas.

**Testitaistelussa** neuvostoryhmän johtajalla on paperi `soviet_orders` ("Soviet orders").

## 4. Tutkiminen

**Käsky:** `LootOrder(sotilas, kaatunut)`.

Hylkäykset:
- kohde ei ole kaatunut tai on tutkija itse: "invalid target"
- kohde on jo tutkittu: "already looted"
- reittiä ei ole: "target not reachable"
- tutkija on lamautettu: "unit is pinned"

**Kulku:**
- Sotilas kulkee kaatuneen luo omalla vauhdillaan, eli ampuu kulkiessaan ja juoksee tulen alla.
- **Tutkiminen alkaa,** kun sotilas on alle 1,5 m päässä ja pysähtynyt. Toiminto `Looting` kestää 40 askelta (2 s), eikä sotilas sillä aikaa liiku eikä ammu.
- **Keskeytys:** lähitaistelu keskeyttää tutkimisen, ja sotilas luopuu siitä. Samoin käy, jos kaatunut on jo tutkittu toisen toimesta.
- **Muut käskyt:** uusi liike-, asento-, pysähdys- tai rynnäkkökäsky peruu tutkimisen.
- **Valmis:** tapahtuma `UnitLooted(tick, tutkija, kaatunut, varalippaat, kranaatit, otettu ase tai null, esineet)`.

**Itsenäinen ryöstö** (joka viides askel). Kaikkien ehtojen pitää täyttyä:
- sotilas on vapaana, ei liiku eikä ole tulen alla
- näkyvää vihollista ei ole alle 60 m päässä
- sotilaalla on ase ja enintään 1 varalipas, tai hänen aseensa on tyhjä
- 10 m säteellä on tutkimaton kaatunut, jolta saa hänelle sopivia patruunoita tai ladatun aseen, eikä kukaan oma ole jo tutkimassa häntä

Sotilas valitsee näistä lähimmän (tasatilanteessa pienempi id).

## 5. Simulaatio

**Askeleen järjestys:** tutkimisen laskuri ja siirto tehdään lähitaistelun jälkeen.

**Tilan tiiviste** kattaa:
- varalippaat
- aseen tunnisteen (ase voi vaihtua)
- esineet
- tutkimuskohteen ja tutkitun tilan

## 6. Näkymä ja ohjaus

- **Vasen klikkaus kaatuneeseen:** kaatuneen täytyy näkyä pelaajalle, ja klikkaukseen pätee sama 1,5 m säde. Komennossa olevista miehistä, jotka eivät ole lamautettuja eikä murtuneita, käskyn saa lähin sellainen, joka voi käyttää kaatuneen patruunoita tai asetta. Jos sellaista ei ole, käskyn saa lähin mies. Tuloksena on `ClickResult.LootOrdered`. Jos kukaan ei pääse lähtemään, tulos on `None`.
- **Näkymättömissä kaatunut** löytyy, kun joku oma näkee hänen paikkansa. Kerran nähty ruumis pysyy näkyvänä.
- **Järjestys:** elävä oma tai vihollinen voittaa kaatuneen.
- **Tutkittu kaatunut:** klikkaus tulkitaan liikkeeksi.
- **Kartalla:**
  - tutkimaton näkyvä kaatunut saa pienen vaalean laukkumerkin
  - tutkimisen jälkeen tutkijan yllä näkyy hetken teksti, esimerkiksi "+2 mags +1 grenade, Soviet orders" tai "nothing"
- **Kortit:** patruunat muodossa `Ammo 5+12` (lippaassa + varalippaita) ja tilat "Looting" ja "Out of ammo".
- **Yläpalkki:** "Papers: Soviet orders", kun joku oma toimintakykyinen mies kantaa papereita.

## 7. Testaus

- **Nmf.Sim:**
  - rajallinen lippaanvaihto ja "patruunat loppu"
  - siirtosäännöt: patruunat, kranaatit, ase ja paperit
  - käsky ja hylkäykset
  - kulku ja kesto
  - keskeytys lähitaistelulla
  - itsenäinen ryöstö ja sen ehdot
  - deterministisyys
- **Nmf.Content:** `spare_magazines` YAMLissa ja oletus. Testitaistelun johtajalla on paperit.
- **Nmf.Client:** klikkaus kaatuneeseen ja järjestys, tilat, ammusteksti ja ryöstöteksti.
- **Godot:** käännös, savutesti ja kuvakaappaus.
