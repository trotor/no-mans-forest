# Valppaus ja hälytys (vaihe 8d, skenaarioiden eepos)

Eepos 3 (spec 2026-10-09-roadmap-epics) vaihe 4 ja pääspecin §9.2–9.3: valppaustilat, hälytyksen leviäminen,
kenttäpuhelin ja etsintäpartio. Käyttäjä 10.10.2026: "tee päätöksiä ja ala toteuttaa".

## 1. Periaate

- Valppaus on **jokaisen miehen oma tila**. Puolen tieto vihollisesta pysyy yhteisenä kuten ennenkin, mutta mies toimii
  vain sen mukaan, mitä hän itse on havainnut tai mistä hänet on hälytetty.
- Tila on simulaation tilaa (`Unit.Alertness`, mukana `StateHash`issa). Se muuttuu vain `Step`in sisällä tai käskyllä,
  joten uusinnat pysyvät samoina.
- **Vanhat tehtävät eivät muutu:** valppaus on käytössä vain puolella, jolle tehtävä sen antaa
  (`enemy_ai: { alertness: calm }`, `Simulation.EnableAlertness(side)`). Silloin puolen miehet aloittavat
  rauhallisina.
  - Muilla puolilla miehet ovat tilassa *taistelu*, eikä mikään tämän specin sääntö koske niitä. Ne eivät huuda,
    eivät ammu raketteja, eivät saa yllätysviivettä, eivät saa havaitsemiskerrointa, eivät suodata komentajaa eivätkä
    muuta partiota.
  - Ilman valppautta ei synny uusia satunnaislukuja eikä tapahtumia. Mittarin tulos pysyy merkki merkiltä samana.
- Pelaajan miehet ovat aina tilassa *taistelu*.

## 2. Tilat

| Tila | Havaitseminen | Toiminta |
|---|---|---|
| Rauhallinen | 40 % | vartioi tai partioi, ei ammu |
| Epäluuloinen | 120 % (katsoo) | pysähtyy, partio odottaa, ei ammu; komentaja voi lähettää tutkimaan; palaa rauhalliseksi, jos 30 s ei tapahdu mitään |
| Hälytetty | 100 % | menee suojaan uhan suuntaan ("asemiin"), huutaa muut hereille, partio loppuu; komentaja voi käyttää häntä |
| Taistelu | 100 % | täysi taistelukäyttäytyminen (nykyinen) |

Tila nousee vain ylöspäin, paitsi epäluuloisesta takaisin rauhalliseksi.

**Yllätys:** rauhallisesta suoraan taisteluun noussut mies ei ammu 2 sekuntiin, epäluuloisesta noussut 1 sekuntiin.
Tämä on väijytyksen ja hiiviskelyn etu.

## 3. Mikä nostaa tilaa

Tarkistus tehdään näkyvyyspäivityksen yhteydessä (0,25 s välein), ja jokainen mies tarkistetaan erikseen.

**Epäluuloinen:**
- Hän kuulee vihollisen liikkeen (liikkeen äänisäde maaston mukaan, kuten nykyinen kuulo).
- Hän näkee jotain: hänellä on näköyhteys viholliseen, jonka havaitseminen on puolella jo vähintään kolmanneksessa.

**Hälytetty:**
- Hän kuulee laukauksen, kummankin puolen, aseen äänisäteen sisällä.
- Räjähdys (kranaatti tai kranaatinheitin) 500 m:n sisällä.
- Käsikähmä 30 m:n sisällä.
- Oman puolen huuto 50 m:n sisällä.
- Valoraketti 600 m:n sisällä.
- Hän näkee kaatuneen toverin 50 m:n sisällä.
- Käsky (`AlertOrder`): esimerkiksi etsintäpartio lähtee liikkeelle hälytettynä.

**Taistelu:**
- Hän näkee itse vihollisen, jonka hänen puolensa näkee.
- Hän joutuu tulen alle (lamautus yli 0) tai haavoittuu.

**Hiljainen taistelu:** vartijan voi kaataa käsikähmässä tai hiipiä ohi. Laukaus hälyttää kaikki kuuloetäisyydellä.

## 4. Hälytyksen leviäminen

- **Huuto:** mies, joka nousee rauhallisesta tai epäluuloisesta hälytetyksi tai taisteluun, huutaa 1 s:n kuluttua.
  Kaikki oman puolen miehet 50 m:n sisällä hälyttyvät, ja he huutavat vuorollaan, joten hälytys kulkee leirin läpi.
  Jokainen mies huutaa enintään kerran, joten ketju ei kierrä.
  Tapahtuma on `AlarmRaised(Shout)`. Pelaajan mies kuulee huudon 150 m:n päähän: siitä tulee merkki ja ilmoitus
  *Vihollinen huutaa hälytystä*.
- **Valoraketti:** tehtävä antaa puolelle raketteja (`enemy_ai: { flares: 2 }`). Johtaja, joka nousee hälytetyksi tai
  taisteluun, ampuu raketin 3 s:n kuluttua, jos puoli ei ole ampunut rakettia 60 s:iin. Kaikki 600 m:n sisällä
  hälyttyvät (`AlarmRaised(Flare)`).
  - Pelaaja näkee raketin aina: ilmoitus *Valoraketti!* ja valopallo kartalla 20 s.
  - Valaiseminen odottaa yötä ja hämärää (§7.3, talvivaihe). Päätös: tässä vaiheessa raketti on vain hälytysmerkki.
- **Kenttäpuhelin:** tehtävä sijoittaa puhelimen ja sen johdon.

  ```yaml
  phones:
    - { id: esikunta, place: puhelin, line: [[410, 300], [470, 250], [599, 230]] }   # place: paikka, tai at: [x, y] m
  ```

  - Kun joku puhelimen puolen mies 30 m:n sisällä puhelimesta on hälytetty tai taistelussa, lähin kunnossa oleva mies
    menee puhelimelle ja soittaa (15 s).
  - Jos johto on ehjä, puhelu menee perille: `PhoneCalled`, ja tehtävän tapahtuma `when: { phone: esikunta }`
    laukeaa, esimerkiksi reservi tai vastaisku. Ehto lukee puhelimen tilaa (`Called`), ei tapahtumaa.
  - Jos johto on poikki, puhelu ei mene perille (`PhoneCallFailed`). Soittajan kaatuminen tai lähteminen keskeyttää
    puhelun, ja seuraava mies yrittää. Puhelin soittaa vain kerran.
  - Puhelin toimii `Step`in sisällä. Se liikuttaa soittajaa suoraan, kuten kantaminen kaukaista auttajaa, eikä anna
    käskyjä. Puhelimen, johdon ja katkaisun tila on mukana `StateHash`issa.
- **Johdon katkaisu:** pelaajan käsky `CutLineOrder(mies, puhelin, kohta)`.
  - Kohdan on oltava 5 m:n sisällä johdosta. Mies menee johdon lähimpään kohtaan ja katkaisee sen 10 s:ssa
    (`LineCut`).
  - Uusi käsky, lamautuminen tai kaatuminen keskeyttää katkaisun.

## 5. Etsintäpartio

Tehtävän tapahtuma `do: { search: { squad: etsinta, zone: hylky } }`:

- Ryhmä lähtee hälytettynä (`AlertOrder`) ja kävelee alueen keskelle.
- Sen jälkeen ryhmä käy alueen läpi 20 m:n ruudukossa, aina lähimpään käymättömään pisteeseen. Miehet kulkevat
  yhdessä, kukin 3 m:n sivuttaisvälein.
- Kun joku ryhmästä on taistelussa tai tulen alla, etsintä loppuu ja ryhmä siirtyy komentajalle (kuten `release`).
- Kilpailu hylystä tehdään nykyisillä ehdoilla: `when: { zone: hylky, side: enemy }`.

Etsintä toimii vuoron ulkopuolella kuten partio: se antaa vain tavallisia käskyjä, ei käytä simulaation
satunnaislukuja ja on mukana käskylokissa.

## 6. Komentaja ja partio

- **Vastaisku:** komentaja ottaa siihen vain hälytettyjä tai taistelussa olevia miehiä.
- **Tutkiminen:** komentaja lähettää tutkimaan vain epäluuloisia tai valppaampia miehiä.
- **Partio:**
  - Rauhallinen mies partioi.
  - Epäluuloinen odottaa.
  - Hälytetystä alkaen partio loppuu. Mies on mennyt suojaan, ja komentaja kutsuu hänet asemaan myöhemmin.
  - Vanhojen tehtävien partiot toimivat kuten ennenkin.

## 7. Pelaajalle

- **Merkki vihollisen yllä,** vain näkyvillä vihollisilla: *?* (keltainen) epäluuloisella ja *!* (punainen)
  hälytetyllä. Kortissa näkyy tila: *Rauhallinen*, *Epäluuloinen* tai *Hälytetty*. Taistelussa olevalla ei ole
  merkkiä, kuten ennenkin.
- **Kartalla:** puhelinjohto ohuena katkoviivana ja puhelin pisteenä. Katkaistu kohta näkyy ruksina. Johto näkyy
  aina, koska käskynjako kertoo siitä.
- **Valikko:** oikealla napilla johdon lähellä *Katkaise puhelinjohto*, kun yksi mies on valittuna (useammasta
  valituista lähin).
- **Ilmoitukset:**
  - *Vihollinen huutaa hälytystä* (kuultu)
  - *Valoraketti!*
  - *Puhelinjohto katkaistu*
  - *Vihollinen soitti puhelimella* (jos joku pelaajan miehistä on 50 m:n sisällä)
  - *Puhelu ei mennyt läpi* (samoin)

## 8. Sisältö

Kehitystehtävä `vartiokoe` (debug):
- Kumpareella on vartio (3 miestä), ja sen vieressä on puhelin. Johto kulkee kartan reunaan.
- Vartion takana on leiri (4 miestä), ja yksi mies partioi.
- Vihollinen on rauhallinen, ja sillä on 1 valoraketti.
- Jos puhelu menee perille, reservi lähtee tulemaan 60 s:n kuluttua.
- 4 minuutin kohdalla etsintäpartio lähtee kohti kohdetta, jonka pelaaja etsii.

`iskuosasto` ja mittari pysyvät ennallaan (ei `alertness`-asetusta).

## 9. Testit

- `AlertnessTests`:
  - vanha oletus ei muutu
  - liikkeen kuuleminen tekee epäluuloiseksi
  - laukaus hälyttää
  - huuto leviää 50 m ketjuna
  - rauhallinen ei ammu, ja yllätysviive on 2 s
  - epäluuloinen palaa rauhalliseksi 30 s:ssa
  - kaatunut toveri hälyttää
  - valoraketti
  - havaitseminen on hitaampaa rauhallisena
  - tulen alla mies siirtyy taisteluun
  - sama siemen antaa saman tuloksen
- `FieldPhoneTests`:
  - hälytetty vartio soittaa, ja puhelu menee perille
  - katkaistu johto estää puhelun
  - katkaisu kestää 10 s, ja uusi käsky keskeyttää sen
  - soittajan kaatuminen keskeyttää puhelun
  - katkaisukohta liian kaukana johdosta hylätään
- `SearchTests`: ryhmä käy alueen läpi ja siirtyy komentajalle taistelussa.
- `PatrolTests`: rauhallinen partioi, epäluuloinen odottaa, hälytetty lopettaa.
- `MissionLoaderTests`: `alertness`, `flares`, `phones`, `phone`-ehto ja `search`-toiminto.
- Client:
  - merkki ja kortin rivi vain näkyvillä vihollisilla
  - valikon rivi
  - ilmoitukset
