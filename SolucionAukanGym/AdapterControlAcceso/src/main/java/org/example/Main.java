package org.example;

import org.apache.activemq.artemis.jms.client.ActiveMQConnectionFactory;
import org.json.JSONObject;

import javax.jms.Connection;
import javax.jms.ConnectionFactory;
import javax.jms.MessageConsumer;
import javax.jms.Queue;
import javax.jms.Session;
import javax.jms.TextMessage;
import java.io.IOException;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;

public class Main {
    private static final String ARTEMIS_URL = "tcp://10.0.0.5:61616";
    private static final String ARTEMIS_USER = "admin";
    private static final String ARTEMIS_PASSWORD = "admin";
    private static final String ARTEMIS_QUEUE = "jdo_amq_estado_cuenta";

    private static final String CONTROL_ACCESO_BASE_URL = "http://localhost:5003/api/users";

    public static void main(String[] args) {
        System.out.println("=== ADAPTER CONTROL DE ACCESO REST - Aukan GYM ===");
        System.out.println("Origen ActiveMQ: " + ARTEMIS_URL);
        System.out.println("COLA ActiveMQ: " + ARTEMIS_QUEUE);
        System.out.println("Destino REST: " + CONTROL_ACCESO_BASE_URL);

        int procesados = 0;
        try {
            ConnectionFactory factory = new ActiveMQConnectionFactory(ARTEMIS_URL);
            try (Connection connection = factory.createConnection(ARTEMIS_USER, ARTEMIS_PASSWORD)) {
                connection.start();

                Session session = connection.createSession(false, Session.AUTO_ACKNOWLEDGE);
                Queue queue = session.createQueue(ARTEMIS_QUEUE);
                MessageConsumer consumer = session.createConsumer(queue);

                while (true) {
                    javax.jms.Message mensaje = consumer.receive(2000);

                    if (mensaje == null) {
                        break;
                    }

                    if (!(mensaje instanceof TextMessage)) {
                        System.out.println("Mensaje ignorado porque no es TextMessage");
                        continue;
                    }

                    String cuerpo = ((TextMessage) mensaje).getText();
                    System.out.println();
                    System.out.println("Mensaje recibido desde ActiveMQ: ");
                    System.out.println(cuerpo);

                    procesarEstadoCuenta(cuerpo);

                    procesados++;
                    System.out.println("Estado aplicado en SistemaControlAcceso: " + procesados);
                }
                consumer.close();
                session.close();
            }
            System.out.println();
            System.out.println("Proceso finalizado correctamente.");
            System.out.println("Total estados procesados:  " + procesados);

        } catch (Exception ex) {
            System.out.println("ERROR en Adapter Control Accesos");
            ex.printStackTrace();
        }

        System.out.println("Presione ENTER para salir");
        try {
            System.in.read();
        } catch (Exception ignored) {
        }
    }

    private static void procesarEstadoCuenta(String jsonEstadoCuenta) throws Exception {
        JSONObject estado = new JSONObject(jsonEstadoCuenta);

        String rutCliente = estado.optString("rutCliente", "").trim();
        String estadoCuenta = estado.optString("estadoCuenta", "").trim().toUpperCase();

        if (rutCliente.isEmpty()) {
            throw new RuntimeException("El mensaje no contiene rutCliente.");
        }

        boolean habilitado = estadoCuenta.equals("AL DIA") || estadoCuenta.equals("AL_DIA");

        if (!usuarioExiste(rutCliente)) {
            crearUsuario(rutCliente, habilitado);
        }

        actualizarHabilitado(rutCliente, habilitado);

        System.out.println("Rut: " + rutCliente);
        System.out.println("Estado cuenta: " + estadoCuenta);
        System.out.println("Habilitado aplicado: " + habilitado);
    }

    private static boolean usuarioExiste(String rut) throws Exception {
        String rutUrl = URLEncoder.encode(rut, StandardCharsets.UTF_8);
        URL url = new URL(CONTROL_ACCESO_BASE_URL + "/" + rutUrl);

        HttpURLConnection conn = (HttpURLConnection) url.openConnection();
        conn.setRequestMethod("GET");

        int status = conn.getResponseCode();
        conn.disconnect();

        return status == 200;
    }

    private static void crearUsuario(String rut, boolean habilitado) throws Exception {
        URL url = new URL(CONTROL_ACCESO_BASE_URL);

        JSONObject usuario = new JSONObject();
        usuario.put("rut", rut);
        usuario.put("nombre", "Cliente " + rut);
        usuario.put("email", rut.replace(".", "").replace("-", "") + "@aukangym.cl");
        usuario.put("telefono", "999999999");
        usuario.put("habilitado", habilitado);

        byte[] body = usuario.toString().getBytes(StandardCharsets.UTF_8);

        HttpURLConnection conn = (HttpURLConnection) url.openConnection();
        conn.setRequestMethod("POST");
        conn.setRequestProperty("Content-Type", "application/json");
        conn.setDoOutput(true);

        try (OutputStream os = conn.getOutputStream()) {
            os.write(body);
        }

        int status = conn.getResponseCode();
        conn.disconnect();

        if (status < 200 || status >= 300) {
            throw new RuntimeException("Errror creando usuario " + rut + ". HTTP " + status);
        }

        System.out.println("Usuario creado en SistemaControlAcceso: " + rut);
    }

    private static void actualizarHabilitado(String rut, boolean habilitado) throws Exception {
        String rutUrl = URLEncoder.encode(rut, StandardCharsets.UTF_8);
        String urlPatch = CONTROL_ACCESO_BASE_URL + "/" + rutUrl + "?habilitado=" + habilitado;

        HttpClient client = HttpClient.newHttpClient();

        HttpRequest request = HttpRequest.newBuilder()
                .uri(URI.create(urlPatch))
                .method("PATCH", HttpRequest.BodyPublishers.noBody())
                .build();

        HttpResponse<String> response =
                client.send(request, HttpResponse.BodyHandlers.ofString());

        int status = response.statusCode();

        if(status < 200 || status >= 300){
            throw new RuntimeException("Errror actualizando usuario "+rut+". HTTP "+status);
        }
    }
}