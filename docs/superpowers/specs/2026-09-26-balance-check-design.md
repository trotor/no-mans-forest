# Tasapainotarkistus (vaihe 4s)

Käyttäjä pyysi 26.9.2026 tarkistamaan tasapainon, koska hyökkäysdemossa suomalaiset kärsivät raskaat tappiot.

## Mittari

Uusi mittari toimii kuten pelaaja. Mittauksessa oli 8 siementä, ja kutakin seurattiin 8 minuuttia kontaktin jälkeen.

- Lähestyminen on joko suora tai tehtävän suunnitelmareitti, jonka viimeiset 160 m hiivitään.
- Näkyvään viholliseen hyökätään. Uusi hyökkäys annetaan aikaisintaan 15 sekunnin kuluttua, eikä maahan painettuja
  miehiä komenneta.
- Kun vihollista ei näy, jatketaan kohti asemaa.

| Tapa | Suomalaisia poissa (7:stä) | Vihollisia poissa (9:stä) | Johtaja kaatui |
|---|---|---|---|
| Suora | 7,0 | 3,4 | 1/8 |
| Suunnitelmareitti | 7,0 | 1,5 | 0/8 |
| Tulitukiasema: tukiryhmä pysähtyy 70 m päähän ja ampuu aluetulta, iskuryhmä hiipii sivusta | 5,9 | 4,9 | 2/8, kerran koko partio |

## Havainnot

- Ensimmäisellä minuutilla kontaktin jälkeen suomalaisista kaatuu 1–6.
- Vartiomiehet kuulevat tulijat ja lähtevät tiedustelemaan, joten kohtaaminen tapahtuu metsässä 26 metrissä.
- Suomalaiset ampuvat noin viidenneksen neuvostopuolen laukausmäärästä. He eivät näe poteroissa ja metsässä olevia.
  Neuvostopuoli taas näkee liikkuvat hyökkääjät ja ampuu aluetulta.
- Kontaktin jälkeen punaiset ovat 97 % ajasta normaalitilassa, siniset vain 75 %.
- Johdattelevat kokeet:
  - Ilman reserviä ja ilman vihollisen oma-aloitteisuutta tulos on yhtä huono.
  - Ampumataidon laskeminen 20 pisteellä parantaa tulosta vain vähän.
  - Vanhallakin kartalla sitkeä hyökkäys epäonnistuu (johtaja kaatui 1–3 kertaa kahdeksasta).
  - Johtopäätös: taistelun rakenne ratkaisee, eivät yksittäiset luvut.
- Aiempi mittari (yksi hyökkäyskäsky ja 5 minuuttia) antoi hyvän kuvan. Se ei jatkanut kohti asemaa, ja sen
  "maali kaatui" tarkoitti ensin nähtyä miestä, ei johtajaa.

## Muutokset (pienet ja realistiset, eivät ratkaise tasapainoa)

- **Ampumasektori:** asema raivaa poterojen ympärille noin 35–39 m pehmeäreunaisen raivion (`clear_field_of_fire`,
  40 m kiekko sumennettuna). Raivion reunalle jää vähän pensaita (noin 2 %).
- **Kokemus havaitsemisessa:** `VisionRules.ObserverPct` = 50 + kokemus. Keskiverto on 100 %, veteraani 140 % ja alokas 80 %.
- **Poteron reuna:** kranaatti, joka räjähtää 1,5 m päässä poterosta (`PitFragmentReachCm`), lähettää sirpaleita sisään.
- **Käsky neuvoo tulitukiaseman:** tukiryhmä pikakiväärin kanssa noin 70 m päähän aluetuleen, iskuryhmä sivustaan.

## Seuraavaksi (ehdotus käyttäjälle)

Varsinainen tasapainovaihe:
- Hyökkäyksen tekoäly perustaisi itse tulitukiaseman kaukaa.
- Poterossa olevan tukahduttaminen aluetulella olisi tehokkaampaa.
- Taistelukyvyttömäksi joutumisen tahtia lähietäisyydellä tarkasteltaisiin.
- Mittari kuuluisi pysyväksi työkaluksi (`tools/`).
