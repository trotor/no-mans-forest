# No Man's Forest – vihollisen aloitteellisuus: vastahyökkäys ja tiedustelu (vaihe 4h)

Päivämäärä: 2026-09-26
Tila: käyttäjän pyyntö ("viholliset voisivat myös yrittää keksiä jotain kehittävää tekemistä ja lähteä vastahyökkäykseen jollain logiikalla, ainakin jos tehtävä sen sallii"), toteutetaan suoraan
Liittyy: hyökkäys `2026-09-26-attack-design.md`, tehtävät `2026-09-26-missions-design.md`, kokemus `2026-09-26-experience-prone-design.md`

## 1. Tavoite

Tekoälypuolella on **komentaja**, joka ei vain puolusta, vaan
- **tiedustelee:** kun se kuulee laukauksia mutta ei näe ampujaa, se lähettää kaksi miestä hiipien katsomaan
- **vastahyökkää:** kun tilanne on sille edullinen, se hyökkää heikoimman nähdyn vastustajan kimppuun samalla tuli- ja liikesuunnitelmalla kuin pelaajan miehet (vaihe 4e)
- **palaa asemiin:** tiedustelun tai vastahyökkäyksen jälkeen, kun vastustajaa ei näy, miehet palaavat asemiinsa

Tehtävä päättää, saako vihollinen toimia näin.

**Rajattu pois:** koukkaukset, vetäytyminen, kranaatinheitintuki ja pelaajan puolen tekoäly.

## 2. mission.yaml

```yaml
enemy_ai:
  counterattack: true   # oletus false
  investigate: true     # oletus false
```

Iskuosastossa molemmat ovat päällä. Käskyn tilanteeseen lisätään maininta, että partio on aktiivinen ja voi yrittää vastaiskua.

## 3. Komentaja (`EnemyCommander`, skenaarion käytös kuten partio)

- **Arviointi:** 2 s välein (40 askelta). Käskyt annetaan tavallisina käskyinä (`AttackOrder`, `MoveOrder`), joten ne ovat käskylokissa ja deterministisiä.
- **Kunnossa oleva mies:** ei taistelukyvytön, vakaa (ei lamautettu tai murtunut), lamautus alle 100 ja patruunoita jäljellä.
- **Tunnettu vastustaja:**
  - näkyvissä ja pystyssä, tai
  - viimeksi nähty enintään 30 s sitten
- **Kaatuneena nähty:** näkyvissä ja taistelukyvytön.

### 3.1 Vastahyökkäys

Alkaa, kun kaikki seuraavat pätevät:

1. Tehtävä sallii vastahyökkäyksen, puolella ei ole hyökkäystä käynnissä, ja edellisen hyökkäyksen päättymisestä on kulunut 60 s.
2. Johtaja on kunnossa.
3. Kukaan puolen miehistä ei ole joutunut tulen alle 10 sekuntiin, eli vastustajan tuli on laantunut.
4. Tunnettuja vastustajia on vähintään yksi, ja kohde on enintään 150 m päässä johtajasta.
5. Voimasuhde on jompikumpi:
   - kunnossa olevia on vähintään 1,5 × tunnetut vastustajat
   - vähintään yksi vastustaja on nähty kaatuneena ja kunnossa olevia on vähintään yhtä monta kuin tunnettuja vastustajia
6. Hyökkääjiä on vähintään kaksi.

**Kohde:** näkyvissä oleva, haavoittunut tai lamautettu vastustaja, muuten johtajaa lähimpänä oleva tunnettu vastustaja. Tasatilanteessa ratkaisee pienin tunnus.

**Hyökkääjät:** kaikki kunnossa olevat, paitsi pikakiväärimies, joka jää asemaan tukemaan tulella, jos kunnossa olevia on vähintään kolme.

**Tapahtuma:** `CounterattackStarted(tick, side, leader, position, target)`, eli johtaja huutaa "Urraa!".

### 3.2 Tiedustelu

Alkaa, kun kaikki seuraavat pätevät:

1. Tehtävä sallii tiedustelun, eikä tiedustelu tai hyökkäys ole käynnissä.
2. Tunnettuja vastustajia ei ole, mutta kuultu (`Suspected`) kontakti on 200 m sisällä johtajasta tai puolen keskipisteestä.

**Tiedustelijat:** kaksi kuultua paikkaa lähintä kunnossa olevaa miestä, ei johtajaa eikä pikakiväärimiestä. He hiipivät paikalle. Tiedustelu päättyy, kun
- he saapuvat perille,
- joku puolesta näkee vastustajan, jolloin he jäävät taistelemaan, tai
- 90 s kuluu.

### 3.3 Paluu asemiin

Kun hyökkäys tai tiedustelu on ohi ja vastustajaa ei ole näkynyt 20 sekuntiin, siihen osallistuneet kunnossa olevat miehet palaavat alkuperäiseen asemaansa omaan tahtiinsa, jos he ovat yli 10 m siitä.

## 4. Näkymä

Kun "Urraa!"-huuto kuuluu, eli joku omista miehistä on 300 m sisällä huutavasta johtajasta:
- keskelle ruutua tulee ilmoitus "Kuuluu huuto: 'Urraa!' — vihollinen hyökkää!"
- kartalle tulee signaali

## 5. Testaus

- **Sim** (`EnemyCommanderTests`):
  - ei toimi, jos tehtävä ei salli
  - ylivoimalla ja tulen laannuttua vastahyökkää heikoimpaan, ja pikakivääri jää asemaan
  - tulen alla odottaa
  - alakynnessä pitää asemansa
  - kaatunut vastustaja ja tasaväkisyys riittää
  - johtajan kaaduttua ei hyökkää
  - tauko hyökkäysten välillä
  - tiedustelu kuultuun paikkaan, ja johtaja pysyy
  - paluu asemiin
  - tapahtuma
- **Content:** `enemy_ai` latautuu ja oletukset ovat false; Iskuosasto sallii molemmat.
- **Client:** ilmoitus kuuluu vain lähellä.
