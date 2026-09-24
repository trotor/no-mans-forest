<?xml version="1.0" encoding="UTF-8"?>
<tileset version="1.10" tiledversion="1.11.2" name="terrain" tilewidth="16" tileheight="16" tilecount="4" columns="4">
 <image source="terrain.png" width="64" height="16"/>
 <tile id="0">
  <properties>
   <property name="terrain" value="grass"/>
   <property name="concealment_per_m" type="float" value="0.05"/>
  </properties>
 </tile>
 <tile id="1">
  <properties>
   <property name="terrain" value="forest"/>
   <property name="concealment_per_m" type="float" value="0.03"/>
   <property name="move_cost" type="float" value="1.3"/>
   <property name="cover" type="float" value="0.1"/>
   <property name="obstacle_height_cm" type="int" value="1500"/>
  </properties>
 </tile>
 <tile id="2">
  <properties>
   <property name="terrain" value="swamp"/>
   <property name="move_cost" type="float" value="2.2"/>
   <property name="concealment_per_m" type="float" value="0.02"/>
  </properties>
 </tile>
 <tile id="3">
  <properties>
   <property name="terrain" value="road"/>
  </properties>
 </tile>
</tileset>
