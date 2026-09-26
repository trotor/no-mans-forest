# Tehtävän kello ja kameran seuraaminen (vaihe 4r)

Käyttäjän toiveet 26.9.2026:
- Pelissä näkyy päivä ja kellonaika, joina tehtävä tapahtuu. Ajanotto säilyy.
- Kamera seuraa omia, mutta karttaa voi siirrellä miesten alueella ilman, että seuraaminen loppuu. Seuraamiselle tulee toggle.
- Käskyyn lisätään, että kannaksella on heinäkuussa yötön yö.

## Kello

- Tehtävään tulee kenttä `start: "yyyy-MM-dd HH:mm"`, paikallista aikaa. Muu muoto on latausvirhe.
  - Iskuosasto alkaa `1942-07-14 03:10`, tiistaina. Karhumäellä aurinko nousee heinäkuun puolivälissä noin kolmelta.
- `MissionClock.Text(start, kulunut, kieli)` palauttaa esimerkiksi "ti 14.7.1942 klo 03:22" tai "Tue 14 July 1942, 03:22".
  - Aika etenee täysin minuutein, ja keskiyön jälkeen vaihtuu päivä.
  - Kellonaika muotoillaan kielestä riippumatta. Suomalainen kieliasetus tekisi siitä muodon "03.22".
  - Jos tehtävässä ei ole alkuhetkeä, kelloa ei näytetä.
- Kello näkyy pienessä tummassa laatassa yläpalkin oikean pään alla. Kulunut aika pysyy yläpalkissa.
- Käskyyn tulee maininta yöttömästä yöstä. Tehtävän päivämäärärivi kertoo päivän ja kellonajan.

## Seuraaminen (L)

- **Toiminta:** `FollowLeash.Centre(keskipiste, puolikas, min, max, sisennys)` akseleittain.
  - Kamera pysyy siellä, minne pelaaja sen siirsi, kunhan komennettujen miesten laatikko pysyy näkymän sisäosassa.
  - Sisennys on 30 % puolikkaasta kummastakin reunasta.
  - Jos miehet kävelevät sisäosasta ulos, kamera siirtyy perään.
  - Jos pelaaja siirtää kameran niin kauas, että miehet katoaisivat, kamera vedetään takaisin.
  - Kun miehet ovat levittäytyneet sisäosaa leveämmälle, rivistöä voi katsoa päästä päähän. Rivistön ohi ei pääse,
    koska sisäosan on pysyttävä joidenkin miesten päällä. Alun perin ryhmä keskitettiin, mutta katselmoinnin jälkeen
    tämä muutettiin, koska keskitys esti katsomasta sivustaa.
  - Näppäimellä pidetty panorointi ei voi viedä miehiä ruudulta: lopuksi kamera rajataan sisennyksellä 0.
  - Tauon aikana seuraaminen ei vedä kameraa, koska silloin pelaaja tiedustelee eivätkä miehet liiku.
    Kamera palaa perään, kun peli jatkuu.
- **Näkyvä alue:** pystysuunnassa lasketaan vain yläpalkin ja korttien väliin jäävä alue (`CameraController.Follow`).
- **Liike:** kamera liukuu kohti tavoitetta pehmeästi (kerroin 1 − e^(−8·dt)).
- **Ketä seurataan:** komennetut miehet (valinta, tai koko joukkue ilman valintaa), jotka ovat vielä taistelukykyisiä, 2 metrin väljyydellä.
- **Päälle ja pois:**
  - Näppäin L tai nappi "Seuraa: päällä / pois (L)" korttipaneelin yläkulmassa.
  - Tila säilyy uusintayrityksen yli.
  - Kun M-kartalla katsoo muualle, seuraaminen menee pois, ja siitä tulee ilmoitus.
  - `--follow` kytkee seuraamisen päälle kuvakaappauksia varten. Hyökkäysdemo käyttää silloin sitä oman kameransa sijaan.

## Testit

- `MissionClockTests`: päivä ja aika, kulunut aika, keskiyön ylitys, ei alkuhetkeä.
- `FollowLeashTests`:
  - Miehet sisällä: kamera ei liiku.
  - Miehet ulos: kamera seuraa.
  - Liian kaukainen siirto vedetään takaisin.
  - Leveää rivistöä voi katsoa päästä päähän, mutta sen ohi ei pääse.
  - Napin teksti.
- `MissionLoaderTests`: `start` luetaan, virheellinen muoto hylätään, ja Iskuosaston alkuhetki on oikea.
