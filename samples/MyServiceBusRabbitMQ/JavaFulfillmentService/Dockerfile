FROM maven:3.9.9-eclipse-temurin-21 AS build

WORKDIR /src
COPY JavaFulfillmentService ./JavaFulfillmentService
RUN mvn -B -f JavaFulfillmentService/pom.xml -DskipTests package

FROM eclipse-temurin:21-jre

WORKDIR /app
COPY --from=build /src/JavaFulfillmentService/target/classes ./classes
COPY --from=build /src/JavaFulfillmentService/target/dependency ./lib
EXPOSE 8080

ENTRYPOINT ["java", "-cp", "/app/classes:/app/lib/*", "com.example.fulfillment.Main"]
