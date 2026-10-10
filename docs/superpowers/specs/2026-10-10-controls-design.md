# Ohjaus: kursori, oikean napin valikko, muuntonäppäimet ja käskyjonot (vaihe 7a)

Käyttäjä 10.10.2026:
- Kursorin pitää kertoa, mitä klikkaus tekee.
- Oikealla napilla pitää saada vaihtoehtoiset toiminnot ja käskyjonot ("ensin hyökätään tähän, sitten liikutaan
  tänne").
- Muuntonäppäimillä pitää saada vakiovaihtoehdot, kuten hyökkää ja mene.

Käyttäjän valinta: oikea nappi avaa valikon kohteen kohdalla. Jono pysähtyy ja kysyy, kun hyökkäys keskeytyy.

## 1. Näppäinkartta

macOS:ssä Cmd toimii kuten Ctrl, ja Option kuten Alt.

| Kohde | Klikkaus | Tuplaklikkaus | Alt | Ctrl | Shift | E pohjassa |
|---|---|---|---|---|---|---|
| Maasto | mene (oma tahti) | juokse | hiivi; Alt + tupla: ryömi | aluetuli tänne | lisää jonoon | hyökkää eteenpäin |
| Vihollinen | ammu | hyökkää (ehdotettu taktiikka) | tupla: pitkä tuli | aluetuli hänen paikkaansa; Ctrl + tupla: suora rynnäkkö | lisää jonoon | – |
| "?" tai viimeksi nähty | aluetuli | hyökkää | – | aluetuli | lisää jonoon | – |
| Kaatunut | (liike) | tutki | – | – | lisää jonoon | – |
| Oma mies | valitse | koko ryhmä | – | – | lisää valintaan | – |

Muutokset nykyiseen:
- **Alt + klikkaus** maastoon oli ryöminen. Nyt se on hiivintä, ja Alt + tupla on ryöminen.
- **Shift + tupla** vihollisen päällä oli suora rynnäkkö. Nyt Shift lisää aina jonoon, ja suora rynnäkkö on
  Ctrl + tupla.
- **"Hyökkää eteenpäin"** on uusi (E pohjassa + klikkaus; A on kameran liike WASD-näppäimissä). Miehet etenevät kohti pistettä
  omaan tahtiinsa. Kun joku heistä näkee vihollisen enintään 150 metrin päässä, he hyökkäävät siihen suunnittelijan
  ehdotuksella. Kun asema on vallattu, eteneminen jatkuu. Jos hyökkäys keskeytyy, jono pysähtyy ja kysyy.
- **Hyökkäys "?"-merkkiin** (tuplaklikkaus) suunnitellaan merkin kontaktia vastaan, kuten nähtyä vihollista vastaan.

## 2. Kursori ja vihje

`ClickPreview.Describe(session, point, modifiers, radius)` (Nmf.Client) kertoo kaksi asiaa: mitä klikkaus tekee
ja mitä tuplaklikkaus tekee. Se käyttää samaa kohteen valintaa kuin `HandleLeftClick` (lähin oma mies, vihollinen,
kaatunut tai merkki). Siksi esikatselu ja klikkaus eivät voi olla eri mieltä.

- **Kursorin muoto** (Godotin valmiit muodot):
  - risti: ammu tai aluetuli
  - osoittava käsi: valitse
  - siirtonuoli: liiku
  - kysymysmerkki: tutki
  - tavallinen nuoli: ei käskyä.
- **Teksti kursorin vieressä**, pienellä fontilla, esimerkiksi *"liiku · tupla: juokse"*, *"ammu · tupla: hyökkää"*,
  *"Shift: jonoon (3.)"*.
  - Teksti näkyy vasta, kun hiiri on ollut paikallaan 0,3 s, jottei se vilku liikkeessä.
  - Se katoaa, kun vihje (EnemyInfo/ContactInfo) näkyy: vihje kertoo enemmän.
- **Asetus "Näytä kursorin ohje"** on oletuksena päällä.

## 3. Oikean napin valikko

`ContextMenu.Items(session, point, radius)` (Nmf.Client) antaa kohteen mukaiset rivit, ja Godot näyttää ne
`PopupMenu`na hiiren kohdalla.

| Kohde | Rivit |
|---|---|
| Maasto | Mene tänne · Juokse · Hiivi · Ryömi · Hyökkää eteenpäin tänne · Aluetuli tänne · Ota asema (katse tänne) |
| Vihollinen tai merkki | Ammu · Hyökkää: \<taktiikat listasta\> · Suora rynnäkkö · Aluetuli |
| Kaatunut | Tutki |
| Oma mies | Valitse · Koko ryhmä · Tyhjennä valinta |
| Tyhjä, ei valintaa | Tyhjennä valinta (kuten ennen) |

- Jokaisen käskyrivin vieressä on *"+ jonoon"*, eli rivi on kahdesti: suoraan ja jonoon.
- Valikko pitää pelin käynnissä, eikä se ole tauko.
- Esc tai klikkaus valikon ohi sulkee sen.
- Uusi käsky **Ota asema (katse tänne)**: miehet hakevat suojan lähellä paikkaansa uhan suuntaa vastaan
  (`CoverFinder`) ja ottavat ampuma-asennon. Se on asemaan jäämisen käsky ilman liikettä kohti.

## 4. Käskyjonot

`OrderQueue` (Nmf.Client) pitää jonon jokaiselle käskettävälle miesjoukolle. Avaimena on miesten tunnusten
järjestetty lista käskyhetkellä. Simulaatio ei tiedä jonoista mitään, koska jono vain antaa tavallisia käskyjä
järjestyksessä. Uusinnat toistavat käskyt, joten determinismi ei muutu.

- **Jonon kasvu:**
  - Shift + käsky lisää käskyn jonon perään.
  - Käsky ilman Shiftiä samoille miehille tyhjentää jonon ja korvaa sen.
  - Jos jono on tyhjä, Shift + käsky aloittaa heti.
- **Valmis**, jolloin seuraava käsky lähtee:
  - liike: kaikki taistelukykyiset ovat perillä (ei liikekohdetta) tai 120 s on kulunut
  - hyökkäys: `PlannedAttackEnded` onnistuneena
  - ammu tai aluetuli: 15 s, tai kohde on poissa taistelusta
  - asema: miehet ovat asennossa
  - "hyökkää eteenpäin": perillä.
- **Pysähtyy ja kysyy,** kun hyökkäys keskeytyy (`PlannedAttackHalted`), käsky hylätään tai joukosta ei ole ketään
  taistelukykyistä. Ruudulle tulee kysymys napeilla:
  *"Hyökkäys pysähtyi: tappioita 2. Jonossa vielä 2 käskyä."* [Jatka jonoa] [Peru jono]
  Peli ei pysähdy kysymykseen, ellei kontaktin tauko ole päällä.
- **Kartalla** jonon käskyt näkyvät numeroituina merkkeinä (1, 2, 3…). Viivat kulkevat miesten keskeltä ensimmäiseen
  käskyyn ja käskystä seuraavaan. Merkki näkyy himmeänä, kun jono on pysähtynyt.
- **Askel kerrallaan.** Jono tarkistetaan joka ruudunpäivityksellä simulaatioaskelten jälkeen
  (`GameSession.Update`).

## 5. Testaus

- `ClickPreviewTests`: jokainen näppäinkartan rivi kohteen ja muuntonäppäimen mukaan. Lisäksi esikatselu ja
  `HandleLeftClick` ovat samaa mieltä (sama kohde, sama tulos).
- `ContextMenuTests`: rivit kohteen mukaan, taktiikat listasta, ja jokainen rivi antaa saman käskyn kuin vastaava
  klikkaus.
- `OrderQueueTests`:
  - lisäys ja korvaus
  - liike valmis, jolloin seuraava lähtee
  - hyökkäys onnistuu, jolloin seuraava lähtee
  - hyökkäys keskeytyy, jolloin jono pysähtyy ja kertoo jäljellä olevat
  - jatka ja peru
  - eri miesjoukoilla on eri jonot
  - kaikki kaatuneet, jolloin jono pysähtyy
- `GameSessionTests`:
  - Alt = hiivi, Alt + tupla = ryömi
  - Ctrl + tupla vihollisen päällä = suora rynnäkkö
  - E + klikkaus = hyökkää eteenpäin: siirtyminen, ja nähtyyn viholliseen hyökkäys
  - hyökkäys "?"-merkkiin.
- Käsin: kursori ja teksti, valikko, jono kartalla ja kysymys testikentällä kuvakaappauksin.
