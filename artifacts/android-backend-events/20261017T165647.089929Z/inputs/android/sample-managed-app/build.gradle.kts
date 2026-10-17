plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

val sampleVersionCode = providers.gradleProperty("sampleVersionCode").orElse("1").get().toInt()

android {
    namespace = "dev.fleet.sample"
    compileSdk = 34
    buildToolsVersion = "34.0.0"

    defaultConfig {
        applicationId = "dev.fleet.sample"
        minSdk = 30
        targetSdk = 34
        versionCode = sampleVersionCode
        versionName = "0.$sampleVersionCode.0"
    }
    buildFeatures {
        buildConfig = true
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
}