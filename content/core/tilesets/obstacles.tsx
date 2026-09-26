<?xml version="1.0" encoding="UTF-8"?>
<tileset version="1.10" tiledversion="1.11.2" name="obstacles" tilewidth="16" tileheight="16" tilecount="3" columns="3">
 <image source="obstacles.png" width="48" height="16"/>
 <tile id="0">
  <properties>
   <property name="obstacle_height_cm" type="int" value="120"/>
   <property name="concealment_per_m" type="float" value="1"/>
   <property name="cover" type="float" value="0.9"/>
   <property name="impassable" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="1">
  <properties>
   <property name="obstacle_height_cm" type="int" value="80"/>
   <property name="concealment_per_m" type="float" value="0.6"/>
   <property name="move_cost" type="float" value="1.5"/>
  </properties>
 </tile>
 <tile id="2">
  <properties>
   <property name="obstacle_height_cm" type="int" value="50"/>
   <property name="concealment_per_m" type="float" value="0.3"/>
   <property name="cover" type="float" value="0.7"/>
   <property name="move_cost" type="float" value="1.6"/>
  </properties>
 </tile>
</tileset>
