plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("com.google.devtools.ksp")
}

dependencies {
    implementation("org.bouncycastle:bcpkix-jdk18on:1.83")
    implementation("com.squareup.moshi:moshi:1.15.2")
    implementation("com.hivemq:hivemq-mqtt-client:1.3.3")
    implementation("androidx.room:room-runtime:2.6.1")
    ksp("androidx.room:room-compiler:2.6.1")
    testImplementation("junit:junit:4.13.2")
    testImplementation("com.squareup.okhttp3:mockwebserver:4.12.0")
    testImplementation("com.squareup.okhttp3:okhttp-tls:4.12.0")
    androidTestImplementation("androidx.test:runner:1.6.2")
    androidTestImplementation("androidx.test.ext:junit:1.2.1")
}

ksp {
    arg("room.schemaLocation", "$projectDir/schemas")
}

android {
    packaging {
        resources.excludes += setOf("META-INF/INDEX.LIST", "META-INF/io.netty.versions.properties",
            "META-INF/versions/9/OSGI-INF/MANIFEST.MF")
    }
    namespace = "dev.fleet.agent"
    compileSdk = 34
    buildToolsVersion = "34.0.0"
    sourceSets.getByName("test").resources.apply {
        srcDir("../../contracts/fixtures")
        include("commands-v1.json", "command-events-v1.json")
    }

    defaultConfig {
        applicationId = "dev.fleet.agent"
        minSdk = 30
        targetSdk = 34
        versionCode = 1
        versionName = "0.1.0"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
}